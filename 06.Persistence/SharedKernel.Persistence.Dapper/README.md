# SharedKernel.Persistence.Dapper

[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue)](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)
![Tier: Adapter](https://img.shields.io/badge/tier-Adapter-6f42c1)
![Public API: tracked](https://img.shields.io/badge/public%20API-tracked-informational)
![PostgreSQL](https://img.shields.io/badge/PostgreSQL-only-4169E1?logo=postgresql&logoColor=white)

> **Hand-written SQL that plays by the platform's rules: a Dapper session that joins the command's transaction, runs
> under the caller's tenant and picks the right database role — and stays plain Dapper.**

| You get | So that |
| --- | --- |
| `IDbSessionFactory.OpenAsync()` joining the ambient unit of work | Dapper and EF Core writes commit or roll back together |
| The caller's tenant bound transaction-locally | Row-level security filters every raw query; no tenant means no rows |
| `OpenReadOnlyAsync()` on the replica, cross-tenant role inside a cross-tenant scope | Each query runs on the right connection without code of its own |
| `session.Command(sql, params, ct)` | Every command carries the transaction and the configured timeout |
| Type handlers for strongly-typed ids, SmartEnums, `jsonb`, `TenantId`, pgvector | Domain types go straight into parameters and out of rows |
| `PostgresErrorMapping.TryAsync` | Constraint and RLS errors become the same `Error` values EF Core produces |

## Contents

- [Install](#install)
- [Quick start](#quick-start)
- [How it works](#how-it-works)
- [Recipes](#recipes)
- [Configuration](#configuration)
- [Reference](#reference)
- [Testing](#testing)
- [Pitfalls](#pitfalls)

## Install

```xml
<PackageReference Include="SharedKernel.Persistence.Dapper" />
```

The version comes from your central `SharedKernelVersion` property — every SharedKernel package ships at the same
version. See [Using the packages](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel#using-the-packages).

| Requirement | Value |
| --- | --- |
| Target framework | `net10.0` |
| Tier | Adapter — reference it from your **Infrastructure** project |
| Depends on | `SharedKernel.Persistence.Npgsql` (declared adapter edge), `SharedKernel.Configuration`, Dapper — **never** EF Core |
| Namespaces | `SharedKernel.Persistence` (registration), `SharedKernel.Persistence.Dapper.Sessions`, `.TypeHandlers`, `.Options` |

## Quick start

```csharp
using SharedKernel.Persistence;

// With EF Core: AddSharedKernelPostgres already registered the data source; add the sessions.
builder.AddSharedKernelPostgres<OrderDbContext>("orders", p => p.UseMultiTenancy(rowLevelSecurity: true));
builder.Services.AddSharedKernelDapper(builder.Configuration);

// Dapper only: register the data source yourself (ConnectionStrings:orders + SharedKernel:Persistence:orders).
builder.Services.AddSharedKernelNpgsql(builder.Configuration, "orders");
builder.Services.AddSharedKernelDapper(builder.Configuration, dapper => dapper
    .AddStronglyTypedId<OrderId, Guid>()
    .AddSmartEnum<OrderStatus, int>()
    .AddJsonb(AppJsonContext.Default.ShippingAddress));
```

```csharp
using Dapper;
using SharedKernel.Persistence.Dapper.Sessions;
using SharedKernel.Persistence.Npgsql.Errors;
using SharedKernel.Primitives.Results;

public sealed class OrderQueries(IDbSessionFactory sessions)
{
    public async Task<OrderRow?> GetAsync(OrderId id, CancellationToken ct)
    {
        await using var session = await sessions.OpenReadOnlyAsync(ct);
        return await session.Connection.QuerySingleOrDefaultAsync<OrderRow>(
            session.Command("SELECT id, status, total_amount FROM orders WHERE id = @id", new { id }, ct));
    }

    public async Task<Result<int>> RenameAsync(OrderId id, string name, CancellationToken ct)
    {
        await using var session = await sessions.OpenAsync(ct);
        var result = await PostgresErrorMapping.TryAsync(() => session.Connection.ExecuteAsync(
            session.Command("UPDATE orders SET name = @name WHERE id = @id", new { id, name }, ct)));
        if (result.IsSuccess)
            await session.CommitAsync(ct);      // a no-op when the session joined a unit of work
        return result;
    }
}
```

Neither query mentions the tenant: with row-level security on, the session bound it and the policy filters every row.

## How it works

| Situation | `OpenAsync` | `OpenReadOnlyAsync` |
| --- | --- | --- |
| Inside `IUnitOfWork.ExecuteInTransactionAsync` (e.g. a command through the transaction behavior) | **Joins** that transaction; `CommitAsync` is a no-op | Joins it too, to read the command's own writes |
| Outside a unit of work | Its own connection and transaction; disposing without `CommitAsync` rolls back | `SET TRANSACTION READ ONLY`, on the replica when `ReadOnlyConnectionString` is configured |
| Row-level security on | The caller's tenant is bound to the transaction in one statement; no tenant → protected tables return nothing | Same |
| Inside an active `ICrossTenantScope` | Opens on the cross-tenant role's data source (6401); refused inside a unit of work | Same |

- `AddSharedKernelDapper` also registers the default `ICrossTenantScope` and, when no `IRequestContext` is registered
  yet, a fail-closed anonymous one (no tenant). Register the real identity (`AddSharedKernelRequestContext()`) in any
  order — the later registration wins.
- Type handlers are process-wide in Dapper; `DapperConfiguration.Apply(...)` is the one place they are set, and the
  `TenantId` handler is always included.
- Dapper emits no spans of its own: Npgsql's `"Npgsql"` source traces every command.

## Recipes

### 1. One transaction for EF Core and Dapper

```csharp
public sealed class PayInvoiceHandler(IRepository<Invoice, InvoiceId> invoices, IDbSessionFactory sessions)
    : ICommandHandler<PayInvoice>
{
    public async Task<Result> Handle(PayInvoice command, CancellationToken ct)
    {
        var invoice = await invoices.GetByIdAsync(command.Id, ct);
        if (invoice is null) return Result.Failure(InvoiceErrors.NotFound(command.Id));

        await using (var session = await sessions.OpenAsync(ct))   // joins the command's transaction
        {
            await session.Connection.ExecuteAsync(session.Command(
                "INSERT INTO payments (id, tenant_id, invoice_id, amount) VALUES (@id, @tenantId, @invoiceId, @amount)",
                new { id = Guid.CreateVersion7(), tenantId = session.RequireTenantId(), invoiceId = command.Id.Value, command.Amount }, ct));
        }

        return invoice.MarkPaid(command.Amount);   // a failed Result rolls back the INSERT too
    }
}
```

### 2. A back-office report across tenants

```csharp
using (crossTenantScope.Enter("revenue by tenant report"))
{
    await using var session = await sessions.OpenReadOnlyAsync(ct);   // the cross-tenant role
    var rows = await session.Connection.QueryAsync<TenantRevenue>(session.Command(
        "SELECT tenant_id, sum(gross_amount) AS gross FROM invoices GROUP BY tenant_id", cancellationToken: ct));
}
```

Needs `RowLevelSecurity:CrossTenantConnectionString` under row-level security — see the
[Npgsql README](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/06.Persistence/SharedKernel.Persistence.Npgsql/README.md).

### 3. Choose isolation or opt out of the ambient transaction

```csharp
await using var session = await sessions.OpenAsync(new DbSessionOptions
{
    IsolationLevel = IsolationLevel.Serializable,
    EnlistInAmbientTransaction = false,
}, ct);
```

## Configuration

| Key | Type | Default | Meaning |
| --- | --- | --- | --- |
| `SharedKernel:Persistence:Dapper:DefaultCommandTimeoutSeconds` | `int?` | `null` (Npgsql's default) | Timeout `session.Command` applies; must be ≥ 1 |

Connection strings, TLS, replica and cross-tenant settings belong to the data source:
`ConnectionStrings:{name}` + `SharedKernel:Persistence:{name}` (see the Npgsql README).

## Reference

| Method / type | Does |
| --- | --- |
| `AddSharedKernelDapper(IConfiguration, Action<DapperConfigurationBuilder>?)` | Binds and validates `DapperPersistenceOptions`, applies type handlers, registers `IDbSessionFactory` (scoped) and `ICrossTenantScope` |
| `AddSharedKernelDapper(Action<DapperConfigurationBuilder>?)` | Same without configuration binding |
| `IDbSessionFactory` | `OpenAsync()`, `OpenReadOnlyAsync()`, `OpenAsync(DbSessionOptions)` → `IDbSession` |
| `IDbSession` | `Connection`, `Transaction`, `Command(sql, parameters, ct)`, `CommitAsync()`, `RequireTenantId()`, `TenantId`, `IsEnlisted`, `IsReadOnly` |
| `DbSessionOptions` | `IsolationLevel`, `ReadOnly`, `EnlistInAmbientTransaction` |
| `DapperConfigurationBuilder` | `AddStronglyTypedId<TId, TValue>(factory?)`, `AddSmartEnum<TEnum, TValue>()`, `AddJsonb<T>(JsonTypeInfo<T>)`, `AddTypeHandler<T>(handler)`, `MatchNamesWithUnderscores(bool)` (on by default) |

Always-registered handlers: `TenantId`/`TenantId?` ↔ `uuid` (an unset `default(TenantId)` is sent as `NULL`), and
pgvector `Vector`, `HalfVector`, `SparseVector` (with `UseVector: true` on the data source).

### Logging

| Event id | Level | Event |
| --- | --- | --- |
| 6400 | Warning | Rolling back an uncommitted session failed (never masks the original error) |
| 6401 | Information | Session opened on the cross-tenant role for an active cross-tenant scope |

## Testing

Test Dapper sessions against real PostgreSQL:
[`SharedKernel.Persistence.Testing`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/16.Testing/SharedKernel.Persistence.Testing/README.md)'s
`PostgresTestServer.StartAsync()` / `PostgresTestDatabase` create a database with the production role split, so
row-level-security claims are proven through an unprivileged role. For handler unit tests that only need a
connection, `FakeDbConnectionFactory` wraps a connection you supply.

## Pitfalls

| Don't | Do | Why |
| --- | --- | --- |
| Interpolate or concatenate SQL | Parameters through `session.Command(sql, new { … }, ct)` | Injection; `SK0042` flags a non-constant SQL argument |
| Open a session outside the command's unit of work for a write | Open it inside the handler | Otherwise the write survives a failed command |
| Open a cross-tenant session inside a unit of work | Run cross-tenant work outside it | The unit of work's transaction runs as the application role; the open throws |
| Take tenant ids from request input | `session.RequireTenantId()` | The bound tenant is the caller's |
| Omit `tenant_id` predicates with row-level security off | Filter on `RequireTenantId()` explicitly | Without RLS nothing else scopes the query |
| Expect rows in a request without a tenant | Give background work a caller with a tenant, or a cross-tenant scope | RLS returns nothing when no tenant is bound — by design |
| Inject `NpgsqlConnection`/`NpgsqlDataSource` in application code | Inject `IDbSessionFactory` | Sessions carry the transaction, tenant and role |

---

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel) ·
[Persistence domain](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/06.Persistence/README.md) ·
[MIT license](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)

# SharedKernel.Persistence.Dapper

[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![Dapper](https://img.shields.io/badge/Dapper-2.1-4B8BBE)](https://github.com/DapperLib/Dapper)
[![PostgreSQL 15+](https://img.shields.io/badge/PostgreSQL-15%2B-4169E1?logo=postgresql&logoColor=white)](https://www.postgresql.org/)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue)](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)
![EF Core: not referenced](https://img.shields.io/badge/EF%20Core-not%20referenced-brightgreen)

> **Hand-written SQL that plays by the platform's rules: it joins the command's transaction, runs under the caller's
> tenant, picks the right database role — and stays plain Dapper.**

Raw SQL is where multi-tenant services leak: a report that forgets `WHERE tenant_id = …`, an insert that commits
even though the command around it failed, a query that runs on the wrong role. `IDbSessionFactory` hands you an open
connection and transaction that are already right — enlisted in the unit of work, bound to the caller's tenant for
row-level security, on the replica for reads, on the cross-tenant role inside a cross-tenant scope. You write ordinary
Dapper on top.

| 🔗 Joins the transaction | 🏢 Tenant-bound | 🎭 Right role | 🧩 Type handlers |
| --- | --- | --- | --- |
| Inside a command, Dapper and EF Core commit or roll back together | The caller's tenant is bound for row-level security | Read-only sessions on the replica | Strongly-typed ids, SmartEnums |
| Outside one, its own transaction | No tenant, no rows | Cross-tenant scope → the cross-tenant role | `jsonb` via source-generated JSON |
| `CommitAsync` is a no-op when enlisted | `RequireTenantId()` for explicit predicates | Never both at once | pgvector types built in |

## Contents

- [Install](#install)
- [Quick start](#quick-start)
- [How a session behaves](#how-a-session-behaves)
- [Recipes](#recipes)
- [Type handlers](#type-handlers)
- [Pitfalls](#pitfalls)
- [AI quick reference](#ai-quick-reference)

## Install

```shell
dotnet add package SharedKernel.Persistence.Dapper
```

| Requirement | Value |
| --- | --- |
| Target framework | `net10.0` |
| Dependencies | Dapper, `SharedKernel.Persistence.Npgsql` — **never** EF Core |
| Namespaces | `SharedKernel.Persistence` (registration), `SharedKernel.Persistence.Dapper.Sessions` (sessions) |

```csharp
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

Unless you registered them, `AddSharedKernelDapper` also adds a fail-closed anonymous `IRequestContext` (no tenant)
and the default `ICrossTenantScope`, so a Dapper-only service runs without EF Core. Register the real identity (for
example `AddSharedKernelRequestContext()`) in any order.

## Quick start

```csharp
using Dapper;
using SharedKernel.Persistence.Dapper.Sessions;

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

## How a session behaves

| Situation | `OpenAsync` | `OpenReadOnlyAsync` |
| --- | --- | --- |
| Inside `IUnitOfWork.ExecuteInTransactionAsync` (e.g. a MediatR command) | **joins** that transaction; Dapper and EF Core writes commit or roll back together; `CommitAsync` is a no-op | joins it too, to read the command's own writes |
| Outside a unit of work | its own connection and transaction; disposing without `CommitAsync` rolls back | `SET TRANSACTION READ ONLY`, on the replica / standby when configured |
| Row-level security on | the caller's tenant is bound to the transaction in one statement; no tenant → protected tables return nothing | same |
| Inside an active `ICrossTenantScope` | opens on the cross-tenant role's data source (refused inside a unit of work, whose transaction runs as the application role) | same |

`session.Command(sql, parameters, ct)` returns a Dapper `CommandDefinition` carrying the transaction and
`SharedKernel:Persistence:Dapper:DefaultCommandTimeoutSeconds`. `session.RequireTenantId()` returns the bound tenant
for SQL that filters on it explicitly (always do so when row-level security is off). `IsEnlisted`, `IsReadOnly` and
`TenantId` describe the session. `OpenAsync(new DbSessionOptions { IsolationLevel = …, ReadOnly = …,
EnlistInAmbientTransaction = … })` covers the rest.

## Recipes

### One transaction for EF Core and Dapper

```csharp
public sealed class PayInvoiceHandler(IRepository<Invoice, InvoiceId> invoices, IDbSessionFactory sessions)
    : ICommandHandler<PayInvoice>
{
    public async Task<Result> Handle(PayInvoice command, CancellationToken ct)
    {
        var invoice = await invoices.GetByIdAsync(command.Id, ct);
        if (invoice is null) return Result.Failure(InvoiceErrors.NotFound(command.Id));

        await using (var session = await sessions.OpenAsync(ct))              // joins TransactionBehavior's transaction
        {
            await session.Connection.ExecuteAsync(session.Command(
                "INSERT INTO payments (id, tenant_id, invoice_id, amount) VALUES (@id, @tenantId, @invoiceId, @amount)",
                new { id = Guid.CreateVersion7(), tenantId = session.RequireTenantId(), invoiceId = command.Id.Value, command.Amount }, ct));
        }

        return invoice.MarkPaid(command.Amount);   // a failed Result rolls back the INSERT too
    }
}
```

### A back-office report across tenants

```csharp
using (crossTenantScope.Enter("revenue by tenant report"))
{
    await using var session = await sessions.OpenReadOnlyAsync(ct);          // the cross-tenant role
    var rows = await session.Connection.QueryAsync<TenantRevenue>(session.Command(
        "SELECT tenant_id, sum(gross_amount) AS gross FROM invoices GROUP BY tenant_id", cancellationToken: ct));
}
```

### Map database errors to `Result`

`PostgresErrorMapping.TryAsync` turns unique, foreign-key, row-level-security and serialization errors into the same
`Error` EF Core's `SaveChanges` produces; compare `Error.Code` with `PostgresClassifiedErrorCodes.*`.

## Type handlers

| Registration | Column |
| --- | --- |
| `AddStronglyTypedId<OrderId, Guid>()` (optional factory for ids without a public constructor) | the underlying value |
| `AddSmartEnum<OrderStatus, int>()` | the underlying value; unknown values throw |
| `AddJsonb(context.Default.T)` | `jsonb`, through the source-generated `JsonTypeInfo<T>` |
| always | pgvector `Vector`, `HalfVector`, `SparseVector` (needs `UseVector: true` in `SharedKernel:Persistence:{name}`) |
| `AddTypeHandler(handler)` | anything else |

`snake_case` columns map to PascalCase properties without aliases (`MatchNamesWithUnderscores(false)` turns it off).
Dapper keeps this configuration process-wide; `DapperConfiguration.Apply(...)` is the one place it is set.

**Telemetry:** Dapper emits no spans of its own — Npgsql's `"Npgsql"` source already traces every command. Logs use
EventId range 6400–6499.

## Pitfalls

| Symptom | Cause and fix |
| --- | --- |
| A query returns no rows in a request | Row-level security is on and the caller has no tenant — expected. Background work needs a caller with a tenant, or a cross-tenant scope |
| A Dapper write survives a failed command | The session was opened outside the command's unit of work. Open it inside the handler |
| `InvalidOperationException` opening a session in a cross-tenant scope | Inside a unit of work the transaction belongs to the application role; run cross-tenant work outside it |
| A write lands in another tenant | Only possible with row-level security off and a missing `tenant_id` predicate — use `RequireTenantId()` |

## AI quick reference

```text
REGISTER     services.AddSharedKernelDapper(builder.Configuration, d => d.AddStronglyTypedId<TId, TValue>()...).
             Dapper-only services first call services.AddSharedKernelNpgsql(builder.Configuration, "name").
INJECT       IDbSessionFactory. Never NpgsqlConnection/NpgsqlDataSource in application code.
SESSION      await using var session = await sessions.OpenAsync(ct) | OpenReadOnlyAsync(ct);
             session.Connection.QueryAsync<T>(session.Command(sql, new { ... }, ct)).
WRITE        Inside a command handler: the session joins the transaction; no CommitAsync needed (it is a no-op).
             Outside: await session.CommitAsync(ct) or it rolls back on dispose.
TENANT       RLS binds the tenant; SQL needs no tenant predicate. Insert tenant_id with session.RequireTenantId().
ERRORS       await PostgresErrorMapping.TryAsync(() => session.Connection.ExecuteAsync(...)) -> Result<int>.
FORBIDDEN    String-interpolated or concatenated SQL (SK0042); tenant ids from request input; opening a
             cross-tenant session inside a unit of work.
```

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel) · start at the
[persistence overview](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/tree/main/06.Persistence).

# SharedKernel.Persistence.Dapper

Dapper on PostgreSQL for Platform.SharedKernel services: an injectable session (open connection + transaction, prepared for the current caller) and the type handlers for SharedKernel types. You write plain Dapper on top. Never references EF Core.

## Register

```csharp
services.AddSharedKernelNpgsql(builder.Configuration, "orders");
services.AddSharedKernelDapper(builder.Configuration, dapper => dapper
    .AddStronglyTypedId<OrderId, Guid>()
    .AddSmartEnum<OrderStatus, int>()
    .AddJsonb(AppJsonContext.Default.ShippingAddress));
```

Also registers — unless you registered them — an anonymous `IRequestContext` (no tenant) and the default `ICrossTenantScope`, so a Dapper-only service runs without EF Core. Register the real identity (e.g. `AddSharedKernelRequestContext()`) in any order.

## Sessions

```csharp
public sealed class OrderQueries(IDbSessionFactory sessions)
{
    public async Task<OrderRow?> GetAsync(OrderId id, CancellationToken ct)
    {
        await using var session = await sessions.OpenReadOnlyAsync(ct);
        return await session.Connection.QuerySingleOrDefaultAsync<OrderRow>(
            session.Command("SELECT id, status, total FROM orders WHERE id = @id", new { id }, ct));
    }

    public async Task<Result<int>> RenameAsync(OrderId id, string name, CancellationToken ct)
    {
        await using var session = await sessions.OpenAsync(ct);
        var result = await PostgresErrorMapping.TryAsync(() => session.Connection.ExecuteAsync(
            session.Command("UPDATE orders SET name = @name WHERE id = @id", new { id, name }, ct)));
        if (result.IsSuccess)
            await session.CommitAsync(ct);
        return result;
    }
}
```

What `OpenAsync` does:

- **Inside a unit of work** (`IUnitOfWork.ExecuteInTransactionAsync`, e.g. a command handler) the session joins that transaction: Dapper and EF Core writes commit or roll back together; `CommitAsync` is a no-op.
- **Otherwise** it opens a connection and its own transaction; dispose without `CommitAsync` rolls back.
- **Read-only** (`OpenReadOnlyAsync`): `SET TRANSACTION READ ONLY`, on the read replica / standby when configured (see the Npgsql README). Inside a unit of work it enlists instead, to read the unit of work's own writes.
- **Row-level security** on: the caller's tenant is bound to the transaction in one statement; no tenant means the protected tables return nothing. Inside an active `ICrossTenantScope` the session opens on the cross-tenant role's data source (refused inside a unit of work, whose transaction runs on the application role).
- `session.Command(...)` adds the transaction and `SharedKernel:Persistence:Dapper:DefaultCommandTimeoutSeconds`.
- `session.RequireTenantId()` gives the tenant for SQL that filters on it explicitly (always do so when RLS is off).
- `PostgresErrorMapping.TryAsync` turns unique/foreign-key/RLS/serialization errors into the same `Error` EF Core's `SaveChanges` produces.

SQL is always parameterized — never interpolate values (`00.Governance` SK0042).

## Type handlers

| Registration | Column |
|---|---|
| `AddStronglyTypedId<OrderId, Guid>()` (optional factory for ids without a public constructor) | the underlying value |
| `AddSmartEnum<OrderStatus, int>()` | the underlying value; unknown values throw |
| `AddJsonb(context.Default.T)` | `jsonb`, via the source-generated `JsonTypeInfo<T>` |
| always | pgvector `Vector`, `HalfVector`, `SparseVector` (needs `SharedKernel:Persistence:Npgsql:UseVector`) |
| `AddTypeHandler(handler)` | anything else |

`snake_case` columns map to PascalCase properties without aliases (`MatchNamesWithUnderscores(false)` turns it off). Dapper keeps this configuration process-wide; `DapperConfiguration.Apply(...)` is the one place it is set.

## Telemetry

Dapper emits no spans of its own: Npgsql's `"Npgsql"` source already traces every command.

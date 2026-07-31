# SharedKernel.Persistence.Dapper

Dapper micro-ORM read-side helpers for Platform.SharedKernel microservices: `StronglyTypedIdTypeHandler<TStronglyTypedId,TValue>`, `SmartEnumTypeHandler<TEnum,TValue>`, `DapperTypeHandlers.Register()`, the `DapperReadService` base (single/multi-mapping queries plus `QueryMultipleAsync`), and `AddSharedKernelDapper()`.

## Included types

- `DapperReadService` — abstract base for read-model query services; `QueryAsync<TResult>` / `QuerySingleOrDefaultAsync<TResult>` / `ExecuteAsync`; multi-mapping `QueryAsync<TFirst,TSecond,TReturn>` / `<TFirst,TSecond,TThird,TReturn>`; `QueryMultipleAsync<TResult>` for multi-statement batches; `protected IDbConnectionFactory ConnectionFactory` as the extension seam
- `StronglyTypedIdTypeHandler<TStronglyTypedId,TValue>` — abstract; consuming services implement a one-line concrete handler per strongly-typed ID
- `SmartEnumTypeHandler<TEnum,TValue>` — abstract; uses `SmartEnum<TEnum,TValue>.TryFromValue`, zero reflection
- `DapperTypeHandlers.Register()` — idempotent, call once at startup
- `AddSharedKernelDapper()` — registers the type handlers

## Install

```xml
<ProjectReference Include="..\SharedKernel.Persistence.Dapper\SharedKernel.Persistence.Dapper.csproj" />
```

This package references `SharedKernel.Persistence.PostgreSQL` for `NpgsqlConnectionFactory` — it does **not** reference `SharedKernel.Persistence.EfCore`, so a service can use Dapper for reads without pulling in EF Core at all.

## Quick start

```csharp
services.AddSharedKernelPostgreSQL(connectionString);   // registers IDbConnectionFactory
services.AddSharedKernelDapper();                       // registers type handlers, idempotent

public sealed class OrderSummaryReadService(IDbConnectionFactory connectionFactory)
    : DapperReadService(connectionFactory)
{
    public Task<IEnumerable<OrderSummaryDto>> GetRecentAsync(int take, CancellationToken ct) =>
        QueryAsync<OrderSummaryDto>(
            sql: "SELECT id, customer_id, total FROM orders ORDER BY created_on DESC LIMIT @take",
            parameters: new { take },
            ct: ct);
}
```

## Multi-mapping join projection

```csharp
public Task<IEnumerable<OrderWithCustomerDto>> GetOrdersWithCustomerAsync(CancellationToken ct) =>
    QueryAsync<OrderRow, CustomerRow, OrderWithCustomerDto>(
        sql: """
             SELECT o.id, o.total, c.id, c.name
             FROM orders o JOIN customers c ON c.id = o.customer_id
             """,
        map: (order, customer) => new OrderWithCustomerDto(order, customer),
        splitOn: "id",
        ct: ct);
```

## Multiple result sets in one round trip

```csharp
public Task<OrderDashboardDto> GetDashboardAsync(CancellationToken ct) =>
    QueryMultipleAsync(
        sql: "SELECT COUNT(*) FROM orders; SELECT SUM(total) FROM orders WHERE status = 'Paid';",
        readFunc: async grid =>
        {
            var count = await grid.ReadSingleAsync<int>();
            var revenue = await grid.ReadSingleAsync<decimal>();
            return new OrderDashboardDto(count, revenue);
        },
        ct: ct);
```

All SQL is caller-supplied and parameterized — string interpolation into SQL is a hard violation enforced across every `DapperReadService` subclass.

## Package

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel) — see [06.Persistence/CLAUDE.md](../CLAUDE.md) for the full interface contracts and implementation rules.

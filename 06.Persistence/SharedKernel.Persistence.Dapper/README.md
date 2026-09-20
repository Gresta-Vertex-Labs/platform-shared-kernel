# SharedKernel.Persistence.Dapper

Dapper micro-ORM read and write helpers for Platform.SharedKernel microservices: `StronglyTypedIdTypeHandler<TStronglyTypedId,TValue>`, `SmartEnumTypeHandler<TEnum,TValue>`, `DapperTypeHandlers.Register()`, the `DapperReadService`/`DapperCommandService` bases (single/multi-mapping queries, `QueryMultipleAsync`, ambient-transaction-enlisted writes) and their tenant-safe, row-level-security-binding counterparts, and `AddSharedKernelDapper()`.

## Included types

- `DapperReadService` — abstract, read-only base for read-model query services; `QueryAsync<TResult>` / `QueryUnbufferedAsync<TResult>` (streaming, `IAsyncEnumerable<TResult>`) / `QueryFirstOrDefaultAsync<TResult>` / `QuerySingleOrDefaultAsync<TResult>` / `ExecuteScalarAsync<TResult>`; multi-mapping `QueryAsync<TFirst,TSecond,TReturn>` / `<TFirst,TSecond,TThird,TReturn>`; `QueryMultipleAsync<TResult>` for multi-statement batches; `protected IDbConnectionFactory ConnectionFactory` as the extension seam. Has no `ExecuteAsync` — a write belongs on `DapperCommandService`
- `DapperCommandService` — abstract base for write commands (`ExecuteAsync`, `ExecuteScalarAsync<TResult>`); enlists in `ITransactionalUnitOfWork`'s ambient connection/transaction when one is active, otherwise opens/commits its own per call. No tenant protection of its own — for a tenant-scoped table use `TenantSafeDapperCommandService` instead
- `TenantSafeDapperReadService` / `TenantSafeDapperCommandService` — the tenant-scoped counterparts; bind the current tenant (and, when active, the `ICrossTenantScope` escape clause) to the database session via `ITenantSessionBinder` for every statement, as defense-in-depth alongside a matching PostgreSQL row-level-security policy; fail closed with `InvalidOperationException` before issuing any SQL when no tenant is resolved and no cross-tenant scope is active
- `StronglyTypedIdTypeHandler<TStronglyTypedId,TValue>` — abstract; consuming services implement a one-line concrete handler per strongly-typed ID
- `SmartEnumTypeHandler<TEnum,TValue>` — abstract; uses `SmartEnum<TEnum,TValue>.TryFromValue`, zero reflection
- `DapperTypeHandlers.Register()` — idempotent, call once at startup
- `AddSharedKernelDapper()` — registers the type handlers

## Install

```xml
<ProjectReference Include="..\SharedKernel.Persistence.Dapper\SharedKernel.Persistence.Dapper.csproj" />
```

This package references only `SharedKernel.Persistence.Abstractions` (plus `01.Core/SharedKernel.Configuration` for its options) — it does **not** reference `SharedKernel.Persistence.PostgreSQL`/`.Npgsql` or `SharedKernel.Persistence.EfCore`, so a service can use Dapper for reads and writes without pulling in EF Core, or even the Npgsql EF Core provider, at all. Register `IDbConnectionFactory` with `SharedKernel.Persistence.Npgsql`'s `AddSharedKernelNpgsql(...)` (or any other implementation).

## Quick start

```csharp
services.AddSharedKernelNpgsql(connectionString);   // registers IDbConnectionFactory
services.AddSharedKernelDapper();                   // registers type handlers, idempotent

public sealed class OrderSummaryReadService(IDbConnectionFactory connectionFactory)
    : DapperReadService(connectionFactory)
{
    public Task<IReadOnlyList<OrderSummaryDto>> GetRecentAsync(int take, CancellationToken cancellationToken) =>
        QueryAsync<OrderSummaryDto>(
            sql: "SELECT id, customer_id, total FROM orders ORDER BY created_on DESC LIMIT @take",
            parameters: new { take },
            cancellationToken: cancellationToken);
}
```

## Multi-mapping join projection

```csharp
public Task<IReadOnlyList<OrderWithCustomerDto>> GetOrdersWithCustomerAsync(CancellationToken cancellationToken) =>
    QueryAsync<OrderRow, CustomerRow, OrderWithCustomerDto>(
        sql: """
             SELECT o.id, o.total, c.id, c.name
             FROM orders o JOIN customers c ON c.id = o.customer_id
             """,
        map: (order, customer) => new OrderWithCustomerDto(order, customer),
        splitOn: "id",
        cancellationToken: cancellationToken);
```

## Multiple result sets in one round trip

```csharp
public Task<OrderDashboardDto> GetDashboardAsync(CancellationToken cancellationToken) =>
    QueryMultipleAsync(
        sql: "SELECT COUNT(*) FROM orders; SELECT SUM(total) FROM orders WHERE status = 'Paid';",
        readFunc: async grid =>
        {
            var count = await grid.ReadSingleAsync<int>();
            var revenue = await grid.ReadSingleAsync<decimal>();
            return new OrderDashboardDto(count, revenue);
        },
        cancellationToken: cancellationToken);
```

All SQL is caller-supplied and parameterized — string interpolation into SQL is a hard violation enforced (by `00.Governance`'s SK0042 analyzer) across every `DapperReadService`, `DapperCommandService`, `TenantSafeDapperReadService`, and `TenantSafeDapperCommandService` subclass.

## Package

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel) — see [06.Persistence/CLAUDE.md](../CLAUDE.md) for the full interface contracts and implementation rules.

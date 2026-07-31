# SharedKernel.Persistence.Abstractions

Zero-ORM persistence contracts for Platform.SharedKernel microservices: `IRepository<TAggregate,TId>` (write-side), `IReadRepository<TAggregate,TId>` (read-side), `IUnitOfWork` / `ITransactionalUnitOfWork`, `IDbConnectionFactory`, and `ISpecificationEvaluator<T>`. **Zero ORM dependencies** — references only `SharedKernel.Primitives`, `SharedKernel.Domain`, and `SharedKernel.Contracts` (for `PagedList<T>`). No `IQueryable<T>` is ever exposed — every query is expressed via `ISpecification<T>` (from `03.Domain`). Implemented by `SharedKernel.Persistence.EfCore`.

Application and domain code should depend on this package's interfaces only — never a concrete ORM type.

## Included types

- `IRepository<TAggregate,TId>` — write-side: `GetByIdAsync`, `GetBySpecAsync`, `AddAsync`, `UpdateAsync`, `DeleteAsync`, `ExistsAsync`, plus bulk `*RangeAsync` siblings
- `IReadRepository<TAggregate,TId>` — read-side: `ListAsync`, `CountAsync`, `AnyAsync`, `GetByIdsAsync`/`GetByIdsChunkedAsync`, `ListPagedAsync`, projection reads (`ListProjectedAsync`, `GetBySpecProjectedAsync`, `ListPagedProjectedAsync`), streaming reads (`StreamAsync`, `StreamProjectedAsync<TResult>`), and keyset/cursor pagination (`ListKeysetAsync<TKey>`)
- `IUnitOfWork` — the single `SaveChangesAsync(CancellationToken)` save boundary
- `ITransactionalUnitOfWork` — extends `IUnitOfWork` with `BeginTransactionAsync`/`ExecuteInTransactionAsync` for explicit, retry-safe multi-repository transactions
- `IDbConnectionFactory` — raw `IDbConnection` source for Dapper and readiness probes, plus `CheckReadinessAsync`
- `ISpecificationEvaluator<T>` — `GetQuery`/`GetProjectedQuery`/`GetKeysetQuery<TKey>`, translating `ISpecification<T>` into a queryable pipeline
- `ByIdSpecification<TAggregate,TId>`, `KeysetPage<TAggregate,TKey>` — supporting specification/result types

## Install

```xml
<ProjectReference Include="..\SharedKernel.Persistence.Abstractions\SharedKernel.Persistence.Abstractions.csproj" />
```

Or, once published, reference the NuGet package `SharedKernel.Persistence.Abstractions` directly, plus a provider package (`SharedKernel.Persistence.EfCore`) to actually resolve an implementation — this package ships no DI extensions and no implementation.

## Design principles

- **Zero-ORM.** No `using Microsoft.EntityFrameworkCore` anywhere in this package — an ORM reference here is a hard architectural violation.
- **No `IQueryable<T>` leakage.** Every read is expressed via `ISpecification<T>`/`IProjectionSpecification<TAggregate,TResult>` — callers never see a raw queryable.
- **One save boundary.** `IUnitOfWork.SaveChangesAsync` is the only permitted way to commit — calling a `DbContext.SaveChanges[Async]` equivalent directly, anywhere outside the implementing package's `EfUnitOfWork`, is a hard violation enforced by the owning domain.
- **`GetByIdsAsync` has no artificial ID-count ceiling.** Against the PostgreSQL implementation this translates to a single `= ANY(@array)` parameter, not a per-value expansion — `GetByIdsChunkedAsync` is an opt-in sibling for callers who deliberately want bounded per-query memory.

## Quick start — write-side repository

```csharp
public sealed class OrderEfRepository(OrderDbContext context)
    : EfRepository<Order, OrderId>(context);

public sealed class CreateOrderHandler(IRepository<Order, OrderId> repository, IUnitOfWork unitOfWork)
{
    public async Task<Result<OrderId>> Handle(CreateOrderCommand command, CancellationToken ct)
    {
        var order = Order.Create(command.CustomerId, command.Lines);
        await repository.AddAsync(order, ct);
        await unitOfWork.SaveChangesAsync(ct);
        return order.Id;
    }
}
```

## Quick start — read-side repository with a specification

```csharp
public sealed class ActiveOrdersSpec : Specification<Order>
{
    public ActiveOrdersSpec()
    {
        AddCriteria(o => o.Status == OrderStatus.Active);
        AddOrderBy(o => o.CreatedOn);
    }
}

var activeOrders = await readRepository.ListAsync(new ActiveOrdersSpec(), ct);
```

## Keyset (cursor) pagination

```csharp
public sealed class OrdersByCreatedOnKeyset(DateTimeOffset? afterKey, object? afterId, int take)
    : KeysetSpecification<Order, DateTimeOffset>(o => o.CreatedOn, o => o.Id, afterKey, afterId, descending: false, take);

var page = await readRepository.ListKeysetAsync(new OrdersByCreatedOnKeyset(null, null, take: 50), ct);
if (page.HasMore)
{
    var next = await readRepository.ListKeysetAsync(
        new OrdersByCreatedOnKeyset(page.NextAfterKey, page.NextAfterId, take: 50), ct);
}
```

## Explicit transactions

```csharp
await transactionalUnitOfWork.ExecuteInTransactionAsync(async ct =>
{
    await orderRepository.AddAsync(order, ct);
    await transactionalUnitOfWork.SaveChangesAsync(ct);
}, ct);
```

`ExecuteInTransactionAsync` is the retry-safe entry point — the delegate may run more than once under a configured retrying execution strategy, so it must be safe to re-run.

## Package

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel) — see [06.Persistence/CLAUDE.md](../CLAUDE.md) for the full interface contracts, implementation rules, and cross-package layering.

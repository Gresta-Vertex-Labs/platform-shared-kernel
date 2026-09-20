# SharedKernel.Persistence.Abstractions

Zero-ORM persistence contracts for Platform.SharedKernel microservices: `IRepository<TAggregate,TId>` (write-side), `IReadRepository<TAggregate,TId>` (read-side), `IUnitOfWork` / `ITransactionalUnitOfWork`, and `IDbConnectionFactory`. **Zero ORM dependencies** — references only `SharedKernel.Primitives`, `SharedKernel.Domain`, and `SharedKernel.Contracts` (for `PagedList<T>`/`CursorPagedList<T>`). No `IQueryable<T>` is ever exposed — every query is expressed via `ISpecification<T>` (from `03.Domain`). Implemented by `SharedKernel.Persistence.EfCore`, which also declares `ISpecificationEvaluator<T>` — the queryable-pipeline translation contract stays out of this zero-ORM package because it is expressed in terms of `IQueryable<T>`.

Application and domain code should depend on this package's interfaces only — never a concrete ORM type.

## Included types

- `IRepository<TAggregate,TId>` — write-side: `GetByIdAsync`, `GetBySpecAsync`, `AddAsync`, `UpdateAsync`, `DeleteAsync`, `ExistsAsync`, plus bulk `*RangeAsync` siblings
- `IReadRepository<TAggregate,TId>` — read-side: `ListAsync`, `CountAsync`, `AnyAsync`, `GetByIdsAsync`/`GetByIdsChunkedAsync`, `ListPagedAsync`, projection reads (`ListProjectedAsync`, `GetBySpecProjectedAsync`, `ListPagedProjectedAsync`), streaming reads (`StreamAsync`, `StreamProjectedAsync<TResult>`), and keyset/cursor pagination (`ListKeysetAsync<TKey>`)
- `IUnitOfWork` — the single `SaveChangesAsync(CancellationToken)` save boundary
- `ITransactionalUnitOfWork` — extends `IUnitOfWork` with `BeginTransactionAsync`/`ExecuteInTransactionAsync` for explicit, retry-safe multi-repository transactions
- `IDbConnectionFactory` — raw `DbConnection` source for Dapper and readiness probes (a concrete `System.Data.Common.DbConnection`, not the `IDbConnection` interface, so callers can `await using` it), plus `CheckReadinessAsync`
- `ByIdSpecification<TAggregate,TId>` — supporting specification type; keyset/cursor reads return `04.Contracts`'s `CursorPagedList<TAggregate>` directly, with no Abstractions-local result type of its own

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

`ListKeysetAsync<TKey>` returns `SharedKernel.Contracts.Pagination.CursorPagedList<TAggregate>` — `Items`, an opaque `NextCursor` (`string?`), and `HasMore` (`NextCursor is not null`). Decode a returned cursor back into the specification's `afterKey`/`afterId` via `PageCursor.Decode<TKey, TId>`:

```csharp
public sealed class OrdersByCreatedOnKeyset(DateTimeOffset? afterKey, object? afterId, int take)
    : KeysetSpecification<Order, DateTimeOffset>(o => o.CreatedOn, o => o.Id, afterKey, afterId, descending: false, take);

var page = await readRepository.ListKeysetAsync(new OrdersByCreatedOnKeyset(null, null, take: 50), ct);
if (page.HasMore)
{
    var position = PageCursor.Decode<DateTimeOffset, Guid>(page.NextCursor).Value;
    var next = await readRepository.ListKeysetAsync(
        new OrdersByCreatedOnKeyset(position.Key, position.Id, take: 50), ct);
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

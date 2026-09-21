# SharedKernel.Persistence.Abstractions

ORM-free persistence contracts for SharedKernel services: repositories, the opaque entity version, bulk mutations,
the cross-tenant scope and the connection factory. Application code depends on these interfaces only;
`SharedKernel.Persistence.EfCore` implements the repositories, `SharedKernel.Persistence.Npgsql` the connection
factory.

No EF Core, Npgsql or Dapper dependency — it references `SharedKernel.Primitives`, `SharedKernel.Domain`
(specifications), `SharedKernel.Contracts` (paging types) and `SharedKernel.Application.Abstractions`.

The unit of work (`IUnitOfWork`), the caller (`IRequestContext`) and the audit writer (`IAuditTrailWriter`) are not
here: they live in `SharedKernel.Application.Abstractions`, shared with the MediatR pipeline, and the persistence
packages implement them directly.

## Repositories

```csharp
using SharedKernel.Persistence.Abstractions.Repositories;
```

| Contract | Tracking | Members |
| --- | --- | --- |
| `IReadRepository<TAggregate, TId>` | never | `GetByIdAsync`, `GetByIdsAsync`, `ExistsAsync`, `FirstOrDefaultAsync(spec)`, `ListAsync(spec)`, `CountAsync(spec)` (`long`), `AnyAsync(spec)`, `ListPagedAsync(spec, PageRequest)`, `ListKeysetAsync(spec, CursorPageRequest, keySelector, descending)`, `StreamAsync(spec)`, and `FirstOrDefaultProjectedAsync`/`ListProjectedAsync`/`ListPagedProjectedAsync`/`ListKeysetProjectedAsync`/`StreamProjectedAsync` for `IProjectionSpecification<TAggregate, TResult>` |
| `IRepository<TAggregate, TId>` : `IReadRepository` | always | tracked `GetByIdAsync`/`FirstOrDefaultAsync`/`ListAsync`; `AddAsync`, `AddRangeAsync`, `UpdateAsync(aggregate[, expectedVersion])`, `UpdateRangeAsync`, `DeleteAsync(aggregate[, expectedVersion])`, `DeleteRangeAsync` |

With `AddSharedKernelPostgres` both are registered for every aggregate of the model — inject them, no subclass needed.
`GetByIdAsync` loads the whole aggregate (put extra `Include`s in a repository subclass's `AggregateQuery()`).
Neither saves: the unit of work (or `TransactionBehavior`) does.

```csharp
public sealed class RenameCustomerHandler(IRepository<Customer, CustomerId> customers) : ICommandHandler<RenameCustomer>
{
    public async Task<Result> Handle(RenameCustomer command, CancellationToken ct)
    {
        var customer = await customers.GetByIdAsync(command.Id, ct);
        if (customer is null) return CustomerErrors.NotFound(command.Id);
        customer.Rename(command.Name);   // tracked: saved and committed by TransactionBehavior
        return Result.Success();
    }
}
```

### Queries are specifications, paging is at the call site

```csharp
var spec = Spec.For<Order>()
    .Where(o => o.Status == OrderStatus.Open)
    .Include(o => o.Lines).ThenInclude(l => l.Product)
    .OrderByDescending(o => o.CreatedOn);

PagedList<Order> page = await orders.ListPagedAsync(spec, PageRequest.Create(page: 2, pageSize: 50).Value, ct);

// Keyset (cursor) pages — for large or actively written sets. The spec must not order; the key selector does.
var open = Spec.For<Order>().Where(o => o.Status == OrderStatus.Open);
CursorPagedList<Order> first = await orders.ListKeysetAsync(open, CursorPageRequest.Create(null, 50).Value, o => o.CreatedOn, descending: true, ct);
CursorPagedList<Order> next = await orders.ListKeysetAsync(open, CursorPageRequest.Create(first.NextCursor, 50).Value, o => o.CreatedOn, descending: true, ct);
```

Specifications (`Specification<T>`, `Spec.For<T>()`, `ProjectionSpecification<T, TResult>`) come from `03.Domain`
(`SharedKernel.Domain.Specifications`). A specification that pages itself, an offset page without an ordering, a
keyset spec that orders, or a nullable keyset key throws `InvalidOperationException`; a malformed cursor throws
`ValidationException` (`pagination.cursor.invalid`).

## Optimistic concurrency: `EntityVersion`

An opaque version (PostgreSQL `xmin`): `ToString()` is the ETag value; `EntityVersion.Parse`/`TryParse` accept
`"42"` and `W/"42"`. Pass the client's `If-Match` to `UpdateAsync(aggregate, version)` / `DeleteAsync(aggregate,
version)`; a stale version fails the save with `ConflictException` (`persistence.concurrency_conflict`). A **detached**
aggregate (deserialized, or loaded in another scope) must use these overloads — it carries no version of its own.
Reading the current version and the full ETag recipe: EfCore README → "Optimistic concurrency with ETag / If-Match".

## Bulk mutations

`IBulkMutationRepository<TAggregate, TId>` updates or deletes matching rows in one statement, without loading them:

```csharp
await bulk.ExecuteUpdateAsync(Spec.For<Order>().Where(o => o.Status == OrderStatus.Draft && o.CreatedOn < cutoff),
    s => s.SetProperty(o => o.Status, OrderStatus.Expired), ct);
await bulk.ExecuteDeleteAsync(spec, ct);   // soft-deletes ISoftDeletable rows (sets IsDeleted/DeletedOn/DeletedBy)
await bulk.ExecutePurgeAsync(spec, ct);    // always physical
```

The spec needs criteria (or `new AllRowsSpecification<T>()` on purpose). Setters targeting the key, a concurrency
token, `TenantId`, `CreatedBy`/`CreatedOn` or an encrypted column are refused; `ModifiedOn`/`ModifiedBy` are
stamped unless you set them. Bulk statements skip the save pipeline and domain events.

## Cross-tenant scope

```csharp
using SharedKernel.Persistence.Abstractions.Context;

using (crossTenantScope.Enter("monthly revenue report"))   // reason required; logged with the caller
{
    // repositories, contexts and Dapper sessions of this DI scope may now read and write every tenant
}
```

The bypass belongs to the dependency-injection scope (request or job) — entered anywhere in it, including inside an
awaited helper, it holds until the handle is disposed, and never leaks to another scope. Under row-level security the
work runs on the cross-tenant database role (see the Npgsql README). `services.AddSharedKernelCrossTenantScope()`
registers it for services that use neither EF Core nor Dapper registrations (both register it themselves).

## Connections

`IDbConnectionFactory.CreateConnectionAsync()` returns an unopened `DbConnection` (use `await using`);
`factory.CheckReadinessAsync(timeout)` is the probe `13.ServiceDefaults`' readiness checks wrap. For SQL, prefer
`SharedKernel.Persistence.Dapper`'s `IDbSessionFactory`, which also joins the unit of work and binds the tenant.

`ITenantSessionBinder`, `IAmbientDbTransaction`, `IMigrationLock` and `IAdvisoryTransactionLock` are infrastructure
seams between the persistence packages (hidden from IntelliSense); application code does not use them.

## Package

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel) — start at the
[06.Persistence README](../README.md); maintainer rules in [06.Persistence/CLAUDE.md](../CLAUDE.md).

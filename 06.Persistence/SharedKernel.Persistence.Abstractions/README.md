# SharedKernel.Persistence.Abstractions

[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue)](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)
![Tier: Abstractions](https://img.shields.io/badge/tier-Abstractions-1f6feb)
![Public API: tracked](https://img.shields.io/badge/public%20API-tracked-informational)
![ORM: none](https://img.shields.io/badge/ORM-none-brightgreen)

> **The persistence contracts application code depends on — repositories, opaque entity versions, bulk mutations
> and explicit cross-tenant access — with no ORM, no database driver and no infrastructure.**

| You get | So that |
| --- | --- |
| `IReadRepository<T,TId>` (never tracks) and `IRepository<T,TId>` (always tracks) | Handlers talk about aggregates and specifications, not `DbContext` |
| Specifications in, `PagedList<T>` / `CursorPagedList<T>` out | Offset and keyset paging, projections and streaming without `IQueryable` leaking |
| `EntityVersion`, an opaque ETag-ready token | `If-Match` works and a stale write is a `ConflictException`, never a leaked row counter |
| `IBulkMutationRepository<T,TId>` | Thousands of rows change in one statement, with protected columns refused |
| `ICrossTenantScope.Enter("reason")` | Cross-tenant work is explicit, scoped to one DI scope and logged with the caller |
| `IDbConnectionFactory` + `CheckReadinessAsync` | Raw connections and a readiness probe without a driver dependency |

## Contents

- [Install](#install)
- [Quick start](#quick-start)
- [How it works](#how-it-works)
- [Recipes](#recipes)
- [Reference](#reference)
- [Testing](#testing)
- [Pitfalls](#pitfalls)
- [Design decisions](#design-decisions)

## Install

```xml
<PackageReference Include="SharedKernel.Persistence.Abstractions" />
```

The version comes from your central `SharedKernelVersion` property — every SharedKernel package ships at the same
version. See [Using the packages](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel#using-the-packages).

| Requirement | Value |
| --- | --- |
| Target framework | `net10.0` |
| Tier | Abstractions — reference it from your **Application** project |
| Depends on | `SharedKernel.Primitives`, `SharedKernel.Execution` (caller, tenant), `SharedKernel.Domain` (specifications), `SharedKernel.Contracts` (paging) |
| Namespaces | `SharedKernel.Persistence.Abstractions.Repositories`, `.Context`, `.Connections`, `.Diagnostics`; registration in `SharedKernel.Persistence` |

No registration is needed: `AddSharedKernelPostgres` in
[`SharedKernel.Persistence.EfCore`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/06.Persistence/SharedKernel.Persistence.EfCore/README.md)
registers the implementations. `IUnitOfWork`, `IRequestContext` and `IAuditTrailWriter` are not here — they live in
`SharedKernel.Execution`, shared with the request pipeline, messaging and jobs.

## Quick start

```csharp
using SharedKernel.Application.Messaging;
using SharedKernel.Contracts.Pagination;
using SharedKernel.Domain.Specifications;
using SharedKernel.Persistence.Abstractions.Repositories;
using SharedKernel.Primitives.Results;

public sealed class RenameCustomerHandler(IRepository<Customer, CustomerId> customers)
    : ICommandHandler<RenameCustomer>
{
    public async Task<Result> Handle(RenameCustomer command, CancellationToken ct)
    {
        var customer = await customers.GetByIdAsync(command.Id, ct);          // tracked: the whole aggregate
        if (customer is null) return Result.Failure(CustomerErrors.NotFound(command.Id));

        customer.Rename(command.Name);
        await customers.UpdateAsync(customer, command.ExpectedVersion, ct);   // optimistic concurrency
        return Result.Success();                                             // the unit of work commits
    }
}

public sealed class ListCustomersHandler(IReadRepository<Customer, CustomerId> customers)
    : IQueryHandler<ListCustomers, PagedList<Customer>>
{
    public async Task<Result<PagedList<Customer>>> Handle(ListCustomers query, CancellationToken ct) =>
        await customers.ListPagedAsync(
            Spec.For<Customer>().Where(c => c.Country == query.Country).OrderBy(c => c.Name), query.Page, ct);
}
```

## How it works

- **Neither repository saves.** The unit of work (`IUnitOfWork.ExecuteInTransactionAsync`, or the application
  pipeline's transaction behavior) commits. `GetByIdAsync` loads the whole aggregate; soft-deleted aggregates are
  hidden unless the specification includes them.
- **Paging happens at the call site.** A specification passed to `ListPagedAsync` orders but never pages itself;
  one passed to `ListKeysetAsync` does not order itself, and the key is non-nullable. Keyset paging seeks
  `(key, id) > (@key, @id)`, never `OFFSET`, so pages stay consistent while rows are inserted.
- **`EntityVersion` is opaque.** The EfCore implementation seals PostgreSQL's `xmin` with the aggregate's identity under
  a key derived from the service's key provider. `ToString()` is the ETag value (28 characters, unpadded Base64Url,
  stable for the same version so `If-None-Match` works); `Parse`/`TryParse`/`IParsable` accept the token, quoted or
  `W/`-prefixed, and check its shape only. A plain number is never a version. A stale, foreign, altered or
  unknown-key token fails the save with `ConflictException` (`persistence.concurrency_conflict`), never a server error.
  `EntityVersion.None` is the version of an aggregate never saved.
- **Bulk statements** are tenant-filtered like every query, stamp `ModifiedOn`/`ModifiedBy`, skip the save pipeline and
  domain events, and refuse setters on a key, a concurrency token, `TenantId`, `CreatedBy`/`CreatedOn` or an
  encrypted column. `ExecuteDeleteAsync` soft-deletes `ISoftDeletable` aggregates; `ExecutePurgeAsync` deletes
  physically.
- **The cross-tenant scope** lasts for the DI scope while the returned `IDisposable` is held; the reason is mandatory
  and logged (6150) with the actor.

## Recipes

### 1. Page through a large or busy set with a cursor

```csharp
var open = Spec.For<Order>().Where(o => o.Status == OrderStatus.Open);   // must not order itself
CursorPagedList<Order> first = await orders.ListKeysetAsync(
    open, CursorPageRequest.Create(null, 50).Value, o => o.CreatedOn, descending: true, ct);
CursorPagedList<Order> next = await orders.ListKeysetAsync(
    open, CursorPageRequest.Create(first.NextCursor, 50).Value, o => o.CreatedOn, descending: true, ct);
```

A malformed cursor is a `ValidationException` (`pagination.cursor.invalid`).

### 2. Project without loading aggregates

```csharp
var rows = await orders.ListPagedProjectedAsync(
    Spec.For<Order>().OrderByDescending(o => o.CreatedOn).Select(o => new OrderRow(o.Id.Value, o.Customer)),
    PageRequest.Create(1, 50).Value, ct);
```

### 3. Update thousands of rows in one statement

```csharp
int expired = await bulk.ExecuteUpdateAsync(
    Spec.For<Order>().Where(o => o.Status == OrderStatus.Draft && o.CreatedOn < cutoff),
    s => s.SetProperty(o => o.Status, OrderStatus.Expired), ct);
```

The specification needs criteria, or `new AllRowsSpecification<Order>()` on purpose.

### 4. Run a back-office job across tenants

```csharp
using (crossTenantScope.Enter("dormant-account review 2026-Q3"))
{
    var dormant = await customers.ListAsync(Spec.For<Customer>().Where(c => c.LastSeenOn < cutoff), ct);
}
```

Under row-level security the work runs on the cross-tenant database role — see the
[EfCore README](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/06.Persistence/SharedKernel.Persistence.EfCore/README.md).
A service with neither EF Core nor Dapper registers the scope itself with `services.AddSharedKernelCrossTenantScope()`.

## Reference

| Contract | Members |
| --- | --- |
| `IReadRepository<TAggregate, TId>` | `GetByIdAsync`, `GetByIdsAsync`, `ExistsAsync`, `FirstOrDefaultAsync(spec)`, `ListAsync(spec)`, `CountAsync(spec)`, `AnyAsync(spec)`, `ListPagedAsync(spec, PageRequest)`, `ListKeysetAsync(spec, CursorPageRequest, keySelector, descending)`, `StreamAsync(spec)`, and `*ProjectedAsync` counterparts for `IProjectionSpecification<TAggregate, TResult>` |
| `IRepository<TAggregate, TId>` | Tracked `GetByIdAsync`, `FirstOrDefaultAsync`, `ListAsync`; `AddAsync`, `AddRangeAsync`, `UpdateAsync(aggregate[, expectedVersion])`, `UpdateRangeAsync`, `DeleteAsync(aggregate[, expectedVersion])`, `DeleteRangeAsync` |
| `IBulkMutationRepository<TAggregate, TId>` | `ExecuteUpdateAsync(spec, Action<BulkUpdateSetters<TAggregate>>)`, `ExecuteDeleteAsync(spec)`, `ExecutePurgeAsync(spec)` |
| `EntityVersion` | `None`, `Parse`, `TryParse`, `ToString()`, `TryFormat`; JSON through `EntityVersionJsonConverter` (a string; `None` is `null`) |
| `ICrossTenantScope` | `IDisposable Enter(string reason)`, `bool IsActive` |
| `IDbConnectionFactory` | `CreateConnectionAsync()` (unopened); `CheckReadinessAsync(timeout)` → `DatabaseReadinessResult(IsHealthy, Latency, Provider, ErrorMessage)` |
| `AddSharedKernelCrossTenantScope()` | Registers `CrossTenantScope` as the scoped `ICrossTenantScope` |

`ITenantSessionBinder`, `IAmbientDbTransaction`, `IMigrationLock` and `IAdvisoryTransactionLock` are infrastructure
seams between the persistence packages, hidden from IntelliSense; application code does not use them.

### Logging

| Event id | Level | Event |
| --- | --- | --- |
| 6150 | Information | Cross-tenant scope entered, with actor id, actor kind, the actor's tenant and the reason |

## Testing

Reference [`SharedKernel.Persistence.Testing`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/16.Testing/SharedKernel.Persistence.Testing/README.md):

```csharp
using SharedKernel.Persistence.Testing;

FakeRepository<Customer, CustomerId> customers = services.AddFakeRepository<Customer, CustomerId>(seed: [existing]);
FakeUnitOfWork unitOfWork = services.AddFakeUnitOfWork();   // TransientFailures proves a handler re-runnable
services.AddFakeCrossTenantScope();
services.AddTestRequestContext();
```

The fake repository applies the same specification, paging and cursor rules as the EfCore implementation, so a
cursor produced by the fake decodes against production. It checks no version: pass `EntityVersion.None`.

## Pitfalls

| Don't | Do | Why |
| --- | --- | --- |
| Change an aggregate loaded through `IReadRepository` | Load it through `IRepository` | Read repositories never track; the change is not saved |
| Order or page inside a keyset specification | Order through `keySelector`, page through `CursorPageRequest` | `ListKeysetAsync` throws `InvalidOperationException` |
| `Skip`/`Take` in a specification passed to `ListPagedAsync` | Pass a `PageRequest`; keep an ordering | Paging belongs at the call site; an unordered or self-paged spec throws |
| `UpdateAsync` a detached aggregate without a version | `UpdateAsync(aggregate, expectedVersion)` with the client's `If-Match` | A detached aggregate carries no version and is refused |
| Expose a numeric row version | Return `EntityVersion.ToString()` as the ETag | The token is opaque; a number never parses |
| Call `SaveChanges` from a handler | Let the unit of work commit | One transaction per DI scope, retried as a whole |
| Expect a bulk update to reach other tenants | Enter a cross-tenant scope on purpose | Bulk statements are tenant-filtered |
| Reference EF Core, Npgsql or Dapper from application code | Depend on these contracts | The implementation is the host's choice |

## Design decisions

**Why is `IQueryable<T>` never exposed?** A repository that returns `IQueryable` hands the ORM to every caller and
makes tenant, soft-delete and tracking rules unenforceable. Specifications keep queries in domain vocabulary.

**Why an opaque version?** PostgreSQL's `xmin` is a database-wide counter: exposing it would leak write rates of a
shared, multi-tenant database and let clients forge versions. Binding the token to its aggregate makes a token of
another aggregate simply stale.

**Why must a cross-tenant scope carry a reason?** Bypassing tenant isolation is the most sensitive thing a service
does; the reason and the caller are logged every time.

---

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel) ·
[Persistence domain](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/06.Persistence/README.md) ·
[MIT license](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)

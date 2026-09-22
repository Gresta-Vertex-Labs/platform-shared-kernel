# SharedKernel.Persistence.Abstractions

[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue)](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)
![ORM: none](https://img.shields.io/badge/ORM-none-brightgreen)
![Public API: tracked](https://img.shields.io/badge/public%20API-tracked-informational)

> **The persistence contracts application code depends on — repositories, versions, bulk mutations, cross-tenant
> access — with no ORM, no database driver and no infrastructure.**

Handlers, domain services and tests should talk about aggregates, specifications and versions, not about `DbContext`,
`NpgsqlConnection` or change trackers. This package is that vocabulary. `SharedKernel.Persistence.EfCore` implements
it on PostgreSQL, `SharedKernel.Persistence.Testing` implements it in memory, and your application layer compiles
against neither.

| 📚 Repositories | 🏷️ Versions | 🧹 Bulk | 🏢 Tenants |
| --- | --- | --- | --- |
| `IReadRepository` never tracks, `IRepository` always does | `EntityVersion`: an opaque, ETag-ready row version | One-statement updates and deletes over a specification | `ICrossTenantScope`: explicit, reasoned, scoped bypass |
| Specifications in, `PagedList`/`CursorPagedList` out | `If-Match` in, `ConflictException` on a stale write | Protected columns cannot be set | Logged with the caller and the reason |
| Streaming and projections | Parses `42` and `W/"42"` | Soft delete aware | Never leaks into another DI scope |

## Contents

- [Install](#install)
- [Quick start](#quick-start)
- [Contracts](#contracts)
- [Recipes](#recipes)
- [Pitfalls](#pitfalls)
- [AI quick reference](#ai-quick-reference)
- [Compatibility and guarantees](#compatibility-and-guarantees)

## Install

```shell
dotnet add package SharedKernel.Persistence.Abstractions
```

| Requirement | Value |
| --- | --- |
| Target framework | `net10.0` |
| Dependencies | `SharedKernel.Primitives`, `SharedKernel.Domain` (specifications), `SharedKernel.Contracts` (paging), `SharedKernel.Application.Abstractions` |
| Not referenced | EF Core, Npgsql, Dapper — none of them, ever |
| Registration | None needed: `AddSharedKernelPostgres` registers the implementations. `services.AddSharedKernelCrossTenantScope()` for a service with neither EF Core nor Dapper |

Reference it from application and domain-service projects. Reference an implementation only from the host.

| Related contract | Lives in | Why not here |
| --- | --- | --- |
| `IUnitOfWork`, `IRequestContext`, `IAuditTrailWriter` | [`SharedKernel.Application.Abstractions`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/tree/main/05.Application/SharedKernel.Application.Abstractions) | Shared with the MediatR pipeline — one contract, no adapter |
| `ISpecification<T>`, `Spec.For<T>()` | [`SharedKernel.Domain`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/tree/main/03.Domain/SharedKernel.Domain) | Queries are domain vocabulary |
| `PagedList<T>`, `CursorPagedList<T>`, `PageRequest` | [`SharedKernel.Contracts`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/tree/main/04.Contracts/SharedKernel.Contracts) | They cross service boundaries |

## Quick start

```csharp
using SharedKernel.Persistence.Abstractions.Repositories;

public sealed class RenameCustomerHandler(IRepository<Customer, CustomerId> customers) : ICommandHandler<RenameCustomer>
{
    public async Task<Result> Handle(RenameCustomer command, CancellationToken ct)
    {
        var customer = await customers.GetByIdAsync(command.Id, ct);   // tracked: the whole aggregate
        if (customer is null) return Result.Failure(CustomerErrors.NotFound(command.Id));

        customer.Rename(command.Name);
        await customers.UpdateAsync(customer, command.ExpectedVersion, ct);   // optimistic concurrency
        return Result.Success();                                             // the unit of work commits
    }
}

public sealed class CustomerListHandler(IReadRepository<Customer, CustomerId> customers) : IQueryHandler<ListCustomers, PagedList<Customer>>
{
    public async Task<Result<PagedList<Customer>>> Handle(ListCustomers query, CancellationToken ct) =>
        Result<PagedList<Customer>>.Success(await customers.ListPagedAsync(
            Spec.For<Customer>().Where(c => c.Country == query.Country).OrderBy(c => c.Name), query.Page, ct));
}
```

Nothing to register: with `AddSharedKernelPostgres` both repositories exist for every aggregate of the model.

## Contracts

### Repositories — `SharedKernel.Persistence.Abstractions.Repositories`

| Contract | Tracking | Members |
| --- | --- | --- |
| `IReadRepository<TAggregate, TId>` | never | `GetByIdAsync`, `GetByIdsAsync`, `ExistsAsync`, `FirstOrDefaultAsync(spec)`, `ListAsync(spec)`, `CountAsync(spec)` (`long`), `AnyAsync(spec)`, `ListPagedAsync(spec, PageRequest)`, `ListKeysetAsync(spec, CursorPageRequest, keySelector, descending)`, `StreamAsync(spec)`, and the `*ProjectedAsync` counterparts for `IProjectionSpecification<TAggregate, TResult>` |
| `IRepository<TAggregate, TId>` : `IReadRepository` | always | tracked reads; `AddAsync`, `AddRangeAsync`, `UpdateAsync(aggregate[, expectedVersion])`, `UpdateRangeAsync`, `DeleteAsync(aggregate[, expectedVersion])`, `DeleteRangeAsync` |
| `IBulkMutationRepository<TAggregate, TId>` | — | `ExecuteUpdateAsync(spec, setters)`, `ExecuteDeleteAsync(spec)` (soft delete for `ISoftDeletable`), `ExecutePurgeAsync(spec)` (physical) |

Neither repository saves: the unit of work (or `TransactionBehavior`) does. `GetByIdAsync` loads the whole aggregate.
Soft-deleted aggregates are hidden unless the specification includes them.

### Versions — `EntityVersion`

An opaque row version (PostgreSQL `xmin` in the EfCore implementation). `ToString()` is the ETag value;
`Parse`/`TryParse` accept `"42"` and `W/"42"`; `EntityVersion.None` is "no version". Pass the client's `If-Match` to
`UpdateAsync(aggregate, version)` / `DeleteAsync(aggregate, version)`; a stale version fails the save with
`ConflictException` (`persistence.concurrency_conflict`).

### Cross-tenant access — `SharedKernel.Persistence.Abstractions.Context`

`ICrossTenantScope.Enter(reason)` returns an `IDisposable`; while it is held, the repositories, contexts and Dapper
sessions of the **same DI scope** may read and write every tenant. The reason is mandatory and logged with the caller.
`IsActive` tells whether a scope is entered.

### Connections — `SharedKernel.Persistence.Abstractions.Connections`

`IDbConnectionFactory.CreateConnectionAsync()` returns an unopened `DbConnection`;
`factory.CheckReadinessAsync(timeout)` returns a `DatabaseReadinessResult` (healthy, latency, provider, error) — the
probe the readiness checks of `SharedKernel.ServiceDefaults.Persistence` wrap.

### Infrastructure seams

`ITenantSessionBinder`, `IAmbientDbTransaction`, `IMigrationLock` and `IAdvisoryTransactionLock` connect the
persistence packages to each other. They are hidden from IntelliSense; application code does not use them.

## Recipes

### Keyset (cursor) paging for large or busy sets

```csharp
var open = Spec.For<Order>().Where(o => o.Status == OrderStatus.Open);        // must not order itself
var first = await orders.ListKeysetAsync(open, CursorPageRequest.Create(null, 50).Value, o => o.CreatedOn, descending: true, ct);
var next  = await orders.ListKeysetAsync(Spec.For<Order>().Where(o => o.Status == OrderStatus.Open),
    CursorPageRequest.Create(first.NextCursor, 50).Value, o => o.CreatedOn, descending: true, ct);
```

The seek predicate is `(key, id) > (@key, @id)` — never `OFFSET` — so pages stay consistent while rows are inserted.
A nullable key is refused; a malformed cursor is a `ValidationException` (`pagination.cursor.invalid`).

### Projections without loading aggregates

```csharp
var rows = await orders.ListPagedProjectedAsync(
    Spec.For<Order>().OrderByDescending(o => o.CreatedOn).Select(o => new OrderRow(o.Id.Value, o.Customer, o.Total.Amount)),
    PageRequest.Create(1, 50).Value, ct);
```

### Update thousands of rows in one statement

```csharp
var expired = await bulk.ExecuteUpdateAsync(
    Spec.For<Order>().Where(o => o.Status == OrderStatus.Draft && o.CreatedOn < cutoff),
    s => s.SetProperty(o => o.Status, OrderStatus.Expired), ct);
```

The specification needs criteria (or `new AllRowsSpecification<Order>()` on purpose). `ModifiedOn`/`ModifiedBy` are
stamped. Setters on a key, a concurrency token, `TenantId`, `CreatedBy`/`CreatedOn` or an encrypted column are
refused. Bulk statements skip the save pipeline and domain events.

### A back-office job across tenants

```csharp
using (crossTenantScope.Enter("dormant-account review 2026-Q3"))
{
    var dormant = await customers.ListAsync(Spec.For<Customer>().Where(c => c.LastSeenOn < cutoff), ct);
}
```

## Pitfalls

| Symptom | Cause and fix |
| --- | --- |
| Changes are not saved | `IReadRepository` never tracks. Change aggregates loaded through `IRepository` |
| `InvalidOperationException` from `ListKeysetAsync` | The specification orders or pages itself, or the key is nullable |
| `InvalidOperationException` from `ListPagedAsync` | The specification has no ordering, or pages itself — paging belongs at the call site |
| `InvalidOperationException` from `UpdateAsync(detached)` | A detached aggregate carries no version; pass the client's with `UpdateAsync(aggregate, expectedVersion)` |
| A bulk update "does nothing" to other tenants | Correct: bulk statements are tenant-filtered like every query |

## AI quick reference

```text
READ         Inject IReadRepository<T,TId>. Never tracks. Spec.For<T>().Where(..).OrderBy(..) + ListPagedAsync(spec,
             PageRequest) | ListKeysetAsync(spec, CursorPageRequest, key, descending) | ListProjectedAsync(spec.Select(..)).
WRITE        Inject IRepository<T,TId>. Always tracks. GetByIdAsync -> mutate -> (UpdateAsync(agg, version) for If-Match).
             Never call SaveChanges; the unit of work or TransactionBehavior commits.
VERSION      EntityVersion.TryParse(ifMatch, out v); ToString() is the ETag value; stale -> ConflictException
             (code persistence.concurrency_conflict).
BULK         IBulkMutationRepository<T,TId>.ExecuteUpdateAsync(spec-with-criteria, s => s.SetProperty(..)); no key,
             TenantId, CreatedBy/On, concurrency-token or encrypted setters.
CROSSTENANT  using (crossTenantScope.Enter("reason")) { ... } — reason required; lasts for the DI scope.
PAGING       A paged spec orders but never pages itself; a keyset spec never orders itself; keys are non-nullable.
FORBIDDEN    Referencing EF Core/Npgsql/Dapper from application code; repository subclasses for queries (use specs);
             Skip/Take in a specification passed to ListPagedAsync.
```

## Compatibility and guarantees

- **No ORM, no driver, no infrastructure** — enforced by architecture tests.
- **Public API is tracked** with `Microsoft.CodeAnalysis.PublicApiAnalyzers`; every public member is documented.
- **Two implementations share one behavior:** `SharedKernel.Persistence.EfCore` (PostgreSQL) and
  `SharedKernel.Persistence.Testing`'s in-memory fake apply the same specification, paging and cursor rules, so a
  cursor produced by the fake decodes against production.

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel) · start at the
[persistence overview](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/tree/main/06.Persistence).

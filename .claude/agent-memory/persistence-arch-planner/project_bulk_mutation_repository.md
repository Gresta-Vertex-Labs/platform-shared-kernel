---
name: project_bulk_mutation_repository
description: IBulkMutationRepository location decision (EfCore not Abstractions), bulk mutation bypass semantics, and hard-physical-delete policy
metadata:
  type: project
---

P-148 introduced `IBulkMutationRepository<TAggregate, TId>` with
`ExecuteUpdateAsync(ISpecification<TAggregate> spec, Expression<Func<SetPropertyCalls<TAggregate>, SetPropertyCalls<TAggregate>>> setPropertyCalls, CancellationToken ct) → Task<int>`
and `ExecuteDeleteAsync(ISpecification<TAggregate> spec, CancellationToken ct) → Task<int>`.

**Location decision:** Lives in `SharedKernel.Persistence.EfCore`, NOT
`SharedKernel.Persistence.Abstractions` — `SetPropertyCalls<TAggregate>` is an
EF Core type (`Microsoft.EntityFrameworkCore.Query`), and putting it in
`.Abstractions` would introduce an ORM dependency there (hard violation).
Implemented directly by `EfRepository<TAggregate, TId>` — no separate base class.

**Bypass semantics (critical, document prominently everywhere):** Both methods
compile to a single server-side `ExecuteUpdate`/`ExecuteDelete` SQL statement that
bypasses the `ChangeTracker` entirely:
- `IUnitOfWork.SaveChangesAsync` is NOT invoked.
- The three platform interceptors (Audit, SoftDelete, Concurrency) do NOT run.
- Domain events are NOT collected or dispatched.

**Hard-physical-delete policy:** `ExecuteDeleteAsync` ALWAYS issues a hard physical
`DELETE`, even when `TAggregate` implements `ISoftDeletable` — there is no
server-side `ExecuteDelete` translation for "set IsDeleted = true". Callers needing
bulk soft-delete must use `ExecuteUpdateAsync` with an explicit `setPropertyCalls`
that sets `IsDeleted`/`DeletedOn`.

**Guard:** `BulkSpecificationGuard.Validate<T>(spec)` throws
`UnsupportedSpecificationException : SharedKernelException` (message:
`"The specification cannot be used with bulk mutation operations: {reason}"`) if the
spec has non-default `Includes`, `StringIncludes`, `OrderBy`/`OrderByDescending`,
`ThenBys`, `Skip`, or `Take`. Only `Criteria`/`IncludeDeleted` are applied by the
evaluator (steps 0 conditional + 1); `IsDistinct`/`AsNoTracking` are tolerated no-ops.

**Why:** Bulk mutations exist for performance-sensitive batch operations (archival,
cleanup jobs) where loading every row into the ChangeTracker is prohibitive. The
trade-off is explicit and must never be silently assumed away by callers.

**How to apply:** Any future "bulk" or "batch" persistence capability that
translates to `ExecuteUpdate`/`ExecuteDelete` must follow this same documentation
pattern — bypass list, hard-delete warning, and spec-shape guard — and must live in
`.EfCore`, never `.Abstractions`, if it depends on an EF Core-only type.

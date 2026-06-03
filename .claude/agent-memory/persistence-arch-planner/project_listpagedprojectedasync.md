---
name: listpagedprojectedasync
description: ListPagedProjectedAsync added to IReadRepository and EfReadRepository (P-101); two round-trips: count via GetQuery, data via GetProjectedQuery; depends on GetProjectedQuery being on the interface (P-097)
metadata:
  type: project
---

`ListPagedProjectedAsync<TResult>(IProjectionSpecification<TAggregate,TResult> spec, CancellationToken ct) → Task<PagedList<TResult>>` added to `IReadRepository` and `EfReadRepository` (P-101, WO-017).

**Why:** "Paged DTO list" is the most common CQRS read pattern. Without it, teams either use `ListPagedAsync` (returns full aggregates, requires in-memory mapping) or manually wire two round-trips with `ListProjectedAsync` + a separate `CountAsync` call. Both workarounds are inconsistent and error-prone.

**How to apply:**
- Count query: call `GetQuery` (no projection applied), strip Skip/Take, call `CountAsync`.
- Data query: call `GetProjectedQuery` (projection applied, Skip/Take applied), call `ToListAsync`.
- Both queries under the same `DbContext` scope (same `EfReadRepository` instance).
- `PagedList<TResult>` constructed from projected items, totalCount, page, pageSize.
- Depends on P-097: `GetProjectedQuery` must be on the `ISpecificationEvaluator<T>` interface — implementation cannot downcast to concrete type.

Related: [[ispecificationevaluator-projection]], [[project_specification_evaluator_order]]

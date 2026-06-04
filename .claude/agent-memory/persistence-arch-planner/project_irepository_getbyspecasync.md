---
name: project_irepository_getbyspecasync
description: IRepository.GetBySpecAsync — write-side tracked fetch by spec; avoids IReadRepository misuse for write paths (P-106)
metadata:
  type: project
---

**P-106 Cap 1 (WO-018, 2026-06-03):** `IRepository<TAggregate, TId>` in `SharedKernel.Persistence.Abstractions` gained `GetBySpecAsync(ISpecification<TAggregate> spec, CancellationToken ct) → Task<TAggregate?>`.

**Why it was needed:** Write-path command handlers need to fetch aggregates by business key (not just PK). Without this, teams either inject `IReadRepository` (wrong — returns non-tracked entities, breaking update semantics) or bypass the abstraction entirely with raw `DbContext` access.

**Tracking rule:** `EfRepository.GetBySpecAsync` calls `ISpecificationEvaluator<T>.GetQuery` then `FirstOrDefaultAsync`. The spec's own `AsNoTracking` flag is honored — but write-side callers must NOT set `AsNoTracking = true`. Setting it returns a detached entity, forcing a full-column UPDATE via `.Update()` instead of change-detection-only updates.

**Hard violation:** Using `IReadRepository.GetBySpecAsync` for write-path fetches. The read repository returns untracked entities by default (specs typically use `ReadOnlySpecification`) — mutations on those entities are not detected by EF change tracking.

**How to apply:** Any write-side command handler that needs to fetch by a non-PK field should inject `IRepository` and call `GetBySpecAsync` with a spec that has `AsNoTracking == false` (the default).

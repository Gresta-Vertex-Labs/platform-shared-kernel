---
name: project_repository_extensions
description: IRepository.ExistsAsync and IReadRepository.GetByIdsAsync — interface extensions added in P-093 (WO-016)
metadata:
  type: project
---

**P-093 decision (WO-016, 2026-06-02):** Two new methods added to the repository contracts in `SharedKernel.Persistence.Abstractions`.

`IRepository<TAggregate, TId>` gains `ExistsAsync(TId id, CancellationToken ct) → Task<bool>`.

`IReadRepository<TAggregate, TId>` gains `GetByIdsAsync(IEnumerable<TId> ids, CancellationToken ct) → Task<IReadOnlyList<TAggregate>>`.

**EF Core implementations:**

- `EfRepository.ExistsAsync` uses `AnyAsync(e => e.Id.Equals(id), ct)` — never materializes the entity.
- `EfReadRepository.GetByIdsAsync` uses `Where(e => ids.Contains(e.Id)).ToListAsync(ct)` — generates `IN (...)` SQL; result order not guaranteed; missing IDs produce no entry; warn callers about >1000 IDs.

**Why:** These are the top two operations causing teams to bypass the repository pattern. `ExistsAsync` prevents full-entity loads for existence checks. `GetByIdsAsync` prevents N+1 loops or direct `DbContext` access for batch loads.

**How to apply:** When planning read-side tasks, prefer `GetByIdsAsync` over looping `GetByIdAsync`. When planning existence checks, always use `ExistsAsync` — never `GetByIdAsync` followed by null check.

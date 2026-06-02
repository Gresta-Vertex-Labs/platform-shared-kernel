---
name: phase-numbering-state
description: Last known phase and work order numbers in the root state-map Phase Backlog
metadata:
  type: project
---

As of 2026-06-02, the last phase written to `state-map.md` Phase Backlog is **P-096** under **WO-016**.

Next new phase must be **P-097**. Next new Work Order must be **WO-017**.

**How to apply:** Always read the current Phase Backlog before assigning new IDs — this memory is a starting point, not a substitute for reading the file.

**WO-016 context:** 06.Persistence and 03.Domain improvement audit. 6 phases across 3 domains:
- P-091 06.Persistence: Audit interceptor string adapter — `IUserContext.UserId` is now `Guid` in Security.Abstractions; interceptors must use `UserId.ToString()` for string audit fields; fallback to `"system"` when unauthenticated
- P-092 06.Persistence: `ICurrentTenantService` → `ITenantProvider` nullability resolution — `Guid?` → `Guid`; `Guid.Empty` is the no-tenant sentinel; documents the zero-rows behavior when no-op provider is registered
- P-093 06.Persistence: Add `ExistsAsync(TId)` to `IRepository` and `GetByIdsAsync(IEnumerable<TId>)` to `IReadRepository` in Abstractions + EfCore implementations
- P-094 06.Persistence: `EfRepository.UpdateAsync` tracking optimization — skip `.Update()` when entity is already tracked; avoids full-column UPDATE statements
- P-095 03.Domain: Add `bool IncludeDeleted` flag to `ISpecification<T>` with `IncludeSoftDeleted()` builder method — prerequisite for P-080 Capability 5 (replaces `QueryableExtensions.IgnoreSoftDeleteFilter`)
- P-096 00.Governance: Architecture rules for GUID format in audit fields and `ExistsAsync`/`GetByIdsAsync` presence on repository implementors

**Domains touched:** 06.Persistence (already ●), 03.Domain (already ●), 00.Governance (already ●) — no `state-map-phase` calls needed; all phases queued in backlog only.

**Key architectural decisions made in WO-016:**
1. Audit string format: `UserId.ToString("D")` (lowercase hyphenated GUID) or `"system"` fallback — not `"N"`, `"B"`, `"P"`, or `"X"` formats
2. `Guid.Empty` is the no-tenant sentinel; `NoOpTenantProvider` returns `Guid.Empty`; TenantedDbContext with no-op returns zero rows — this is intentional and safe
3. `IQueryable` leak through `QueryableExtensions.IgnoreSoftDeleteFilter` must be closed via `IncludeDeleted` flag on `ISpecification<T>` (P-095 → enables P-080 Capability 5)
4. `EfRepository.UpdateAsync` must check `DbContext.Entry(entity).State` before calling `.Update()` — unconditional `.Update()` is a known anti-pattern

---
name: project_efrepository_update_optimization
description: EfRepository.UpdateAsync tracking optimization — skip .Update() for already-tracked entities (P-094, WO-016)
metadata:
  type: project
---

**P-094 decision (WO-016, 2026-06-02):** `EfRepository<TAggregate, TId>.UpdateAsync` must check entity tracking state before calling `.Update(aggregate)`.

Rule:

- If `DbContext.Entry(aggregate).State != EntityState.Detached`: do NOT call `.Update()`. EF Core change detection handles dirty tracking automatically. This avoids full-column UPDATE statements.
- If `EntityState.Detached`: call `.Update(aggregate)` as before. All columns are marked Modified.

**Helper:** `protected virtual void MarkAsModifiedIfDetached(TAggregate aggregate)` encapsulates this logic.

**Why:** Unconditional `.Update()` generates full-column UPDATE statements even when only one property changed. At shared-kernel scale (hundreds of services), this is measurable write overhead.

**How to apply:** When planning any EfRepository implementation task, always include the tracking-state check in `UpdateAsync`. The detached path (`.Update()`) remains unchanged for consumers that construct and attach entities outside the DbContext lifetime.

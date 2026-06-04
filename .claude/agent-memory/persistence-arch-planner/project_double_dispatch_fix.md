---
name: project_double_dispatch_fix
description: EfTransactionalUnitOfWork dispatch-deferral rule — CurrentTransaction check prevents duplicate domain events (P-105)
metadata:
  type: project
---

**P-105 Fix 1 (WO-018, 2026-06-03):** `EfTransactionalUnitOfWork.SaveChangesAsync` had a double-dispatch bug: it called `DispatchAndClearEventsAsync` unconditionally, then `EfPersistenceTransaction.CommitAsync` also dispatched. Any `BeginTransactionAsync → SaveChangesAsync → CommitAsync` flow produced duplicate domain events.

**The fix — CurrentTransaction guard:**

- When `DbContext.Database.CurrentTransaction` is **non-null** (active explicit transaction): call only `DbContext.SaveChangesAsync(ct)`. Do NOT dispatch. Dispatch is deferred to `EfPersistenceTransaction.CommitAsync` which fires after the DB commit succeeds.
- When `DbContext.Database.CurrentTransaction` is **null** (no open transaction): call `DbContext.SaveChangesAsync(ct)` then `DispatchAndClearEventsAsync(ct)` immediately. Matches `EfUnitOfWork` semantics.
- `EfPersistenceTransaction.RollbackAsync` must NOT dispatch events.
- `EfUnitOfWork.SaveChangesAsync` (non-transactional) is NOT affected — its dispatch-after-save behavior is correct and unchanged.

**Why:** At platform scale, duplicate domain events cause idempotency failures, duplicate message publishing, and audit anomalies. The bug is invisible in unit tests (which mock the dispatcher) and only surfaces in integration tests or production.

**How to apply:** Any plan task that adds or modifies `EfTransactionalUnitOfWork` or `EfPersistenceTransaction` must preserve this guard. The three canonical test scenarios are: begin→save→commit dispatches once; save without transaction dispatches once; begin→save→rollback dispatches never.

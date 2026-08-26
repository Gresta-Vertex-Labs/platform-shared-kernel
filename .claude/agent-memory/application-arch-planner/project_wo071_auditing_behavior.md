---
name: project_wo071_auditing_behavior
description: WO-071/P-458 AuditingBehavior design (locked 2026-08-26) — twelve-step pipeline, fifth local seam, no cross-domain Core blocker
metadata:
  type: project
---

WO-071 (root Phase Backlog P-458, depends on `06.Persistence`'s P-456) adds an opt-in `AuditingBehavior<TRequest,TResponse>` to `SharedKernel.Application.Behaviors`, feeding `06.Persistence`'s new append-only, hash-chained audit trail (`IAuditTrailWriter`/`AuditEntry`/`AuditRecord`/`IAuditActorContext`, P-456/P-457/WO-071, design-locked by `persistence-arch-planner` the same session, 2026-08-26).

**Status as of 2026-08-26: Design dispatched, all six phases `○` in `05.Application/state-map.md` (D-81..D-89, S-23, C-82..C-87, T-74..T-78, DO-28..DO-30, P-27..P-30 — 28 tasks). Nothing implemented yet.**

## Shape locked

- `IAuditableRequest<TResponse>` — marker, mirrors `ILoggableRequest<TResponse>` exactly: immediate `Action`/`ResourceType`/`ResourceId`/`BeforeSnapshot` properties (known at construction time) + `GetAfterSnapshot(TResponse response)` method (invoked only after `next()` returns normally, never on a thrown exception). All values opaque caller-pre-serialized strings — never parsed/diffed by this package.
- `IAuditTrailWriter` (local seam) — `RecordAsync(AuditEntry entry, CancellationToken ct = default) → Task`. Same name as `06.Persistence.Abstractions`'s real contract (deliberate, mirrors `IUnitOfWork` precedent), deliberately smaller: no `Id`/`ActorId`/`TenantId`/`OccurredOn`/`CorrelationId`/hash-chain — all resolved by the real writer.
- `AuditEntry` (local record) — `Action`, `ResourceType`, `ResourceId`, `BeforeSnapshot`, `AfterSnapshot`, `ApprovalId` (all populated by `AuditingBehavior`, never by the caller directly for `ApprovalId`).
- `AuditingBehavior<TRequest,TResponse>` — `where TRequest : ICommandBase, IAuditableRequest<TResponse>, IRequest<TResponse>` (commands only, no runtime `is`-check needed — DI generic-constraint resolution excludes non-matching closed types automatically, same as every other commands-only behavior here). Calls `next()` first, then unconditionally records on BOTH success and business-failure outcomes (never on a thrown exception). Does NOT catch an exception from `RecordAsync` itself — propagates and blocks the pipeline (fails closed).
- Dual-approval linkage: `request is IRequiresDualApproval dual` → `ApprovalId = dual.ApprovalKey`. Zero coupling to `IDualApprovalStore`.

## Pipeline placement — the key design decision

Inserted as the **new step 10**, between `IdempotentCommandBehavior` (step 9, unchanged) and `TransactionBehavior` (renumbered 10→11; `CacheInvalidationBehavior` renumbered 11→12). Pipeline grows from eleven to **twelve** named slots.

Positioning rationale ("just inside Transaction, after the result is known but before/alongside commit," per the phase's own acceptance criteria): Auditing's write must complete BEFORE `TransactionBehavior`'s commit executes — the temporal MIRROR-IMAGE of `CacheInvalidationBehavior` (which runs strictly AFTER a confirmed commit). Realized by registering `AuditingBehavior` CLOSER to the actual handler than `TransactionBehavior` in the real DI chain — the same inverted-registration-order technique `CacheInvalidationBehavior` already needed (numbered-list "step order" in this domain's docs tracks the LOGICAL/TEMPORAL sequence of when each behavior's meaningful post-`next()` action fires, not literal DI-registration order; post-`next()` logic executes in REVERSE of registration order since MediatR's pipeline aggregates via `.Reverse()` — first-registered is outermost/last-to-run-its-post-logic, last-registered is innermost/first-to-run-its-post-logic). Don't assume "step N" in this domain's canonical-order table equals "registered Nth" — check the positional rationale prose for behaviors whose meaningful work happens in post-`next()` logic (Transaction/CacheInvalidation/now Auditing).

## Genuinely new finding: no cross-domain Core blocker

Unlike `TransactionBehavior`/`AuthorizationBehavior`/`IdempotentCommandBehavior`/`DualApprovalBehavior`, this phase's Core implementation is **not blocked** on the real `06.Persistence` implementation shipping — the local seam is fully self-contained. Only the eventual composition-root bridge adapter (a consuming-service concern) needs `06.Persistence`'s P-456/P-457 to exist. Recorded as an informational-only Cross-Domain Dependencies row, not a blocker.

## Downstream

Root Phase Backlog P-459 (`16.Testing` fakes for both the real `06.Persistence` seam and this local seam) depends on P-457 AND P-458 — out of this domain's jurisdiction, tracked but not acted on.

See [[pattern_local_seam_bridging]] for how this fits the established five-instance pattern.

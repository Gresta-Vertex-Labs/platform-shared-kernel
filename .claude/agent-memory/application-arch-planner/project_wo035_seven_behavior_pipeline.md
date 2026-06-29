---
name: project_wo035_seven_behavior_pipeline
description: WO-035 (root P-214–P-219) gold-standard build-out — full seven-step pipeline design locked in 05.Application/CLAUDE.md, design-only as of 2026-06-29
metadata:
  type: project
---

WO-035 dispatched 2026-06-29 (root `state-map.md` P-214–P-219) is the gold-standard build-out for `05.Application`: command/query vocabulary, the domain-event-to-MediatR bridge (fulfilling `03.Domain` P-081), and a **seven-step** pipeline behavior suite — the original five (Validation/Logging/Metrics/Transaction/Caching) plus two new ones, **AuthorizationBehavior** and **IdempotentCommandBehavior**.

**Why:** A platform serving hundreds of services without authorization or idempotency at the in-process command boundary leaves the two most universally hand-rolled-per-handler concerns unaddressed. Both follow the exact same "local seam" pattern `TransactionBehavior`'s `IUnitOfWork` already established — never a direct reference to the real infrastructure package (`12.Security`, `07.Messaging`), because `05.Application`'s layering ceiling is `01–04`.

**How to apply:** As of 2026-06-29 this is **design-only** — `05.Application/CLAUDE.md` and `state-map.md` (Design phase D-01..D-10) are written, but Scaffold/Core/Tests/Docs/Published (S/C/T/DO/P task IDs) are still `○` pending. Before recommending or referencing any of the new types below as if they exist in code, verify the state-map phase status first — check `05.Application/state-map.md` Package Board and Overall Progress table, not just CLAUDE.md (CLAUDE.md describes the target design, not necessarily what's implemented).

**Locked design decisions (see [[pipeline_behavior_local_seam_pattern]] for the seam pattern itself):**

1. **Canonical pipeline order is now seven steps**: Logging → Metrics → Validation → Authorization → Caching → Idempotency → Transaction. Authorization sits right after Validation (don't spend a permission check on garbage input) and before everything mutating/caching. Idempotency sits innermost-but-one, immediately outside Transaction (duplicate detection must happen right before commit, not earlier where another behavior could still intervene).
2. **`AuthorizationBehavior` applies to commands AND queries** — constrained only to `IAuthorizeRequest`, not `ICommandBase`. This is different from Caching/Transaction/Idempotency, which are each constrained to one request shape only. Rationale: queries can need permission checks too (e.g. "view another tenant's data").
3. **`IdempotentCommandBehavior` is commands-only** (`ICommandBase` constraint), mirroring `TransactionBehavior` exactly. Never applies to queries — queries already have `ICacheableQuery<TResponse>` for their own orthogonal "don't redo work" concern.
4. **`AuthorizationBehavior` never throws** — short-circuits with `Result.Failure(Error.Unauthorized(...))`, consistent with the established rule that exceptions are reserved for `ValidationException` and genuinely unexpected faults.
5. **`IdempotentCommandBehavior` does NOT cache/replay the original response payload** on a duplicate — it returns `Result.Failure(Error.Conflict(...))` instead. This was a deliberate scope-narrowing decision (documented in P-214/P-217), not an oversight — replaying a stored `Result<T>` would need a generic serialization concern this behavior doesn't take on.
6. Root P-015 (WO-004)'s `CachingBehavior`/`ICacheableQuery<TResponse>` design was carried forward **verbatim, no redesign** — already correct, just needed dispatching alongside the rest.

Root backlog IDs: P-214 (Design), P-215 (Scaffold), P-216 (Core — vocabulary/bridge), P-217 (Core — behaviors suite), P-218 (Tests), P-219 (Docs+Published). All under WO-035.

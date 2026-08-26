---
name: pattern_local_seam_bridging
description: The repeated "local seam interface, bridged at composition root" pattern this domain uses for IUnitOfWork, IAuthorizationContext, IIdempotencyKeyStore
metadata:
  type: project
---

`05.Application`/`05.Application.Behaviors` cannot reference `06.Persistence`, `07.Messaging`, or `12.Security` — the layering ceiling is `01–04`. Whenever a pipeline behavior needs a capability that's "really" owned by one of those higher layers, the fix is always the same pattern, not a new one each time:

1. Define a **minimal local interface** in `SharedKernel.Application.Behaviors` exposing only what the behavior itself needs (never the full real interface's surface).
2. The behavior depends on the local interface only.
3. The **consuming service bridges** the local seam to the real implementation via a one-line adapter registered at the **composition root** — never inside this package.

Five instances of this pattern exist (as of WO-071, 2026-08-26):

| Behavior | Local seam | Bridges to | Real package |
| --- | --- | --- | --- |
| `TransactionBehavior` | `IUnitOfWork` (`SaveChangesAsync` only) | `IUnitOfWork` | `06.Persistence.Abstractions` |
| `AuthorizationBehavior` | `IAuthorizationContext` (`IsAuthorizedAsync(string, ct)`/`AllOf`/`AnyOf`) | `IUserContext`/`ITenantProvider` | `12.Security.Abstractions` |
| `IdempotentCommandBehavior` | `IIdempotencyKeyStore` (`HasProcessedAsync`/`MarkProcessedAsync`, mirrors shape but not identity of `07.Messaging`'s `IIdempotencyStore`) | (consuming service's own store) | `07.Messaging.Abstractions` (shape-only precedent, not a reference) |
| `DualApprovalBehavior` (WO-058, shipped) | `IDualApprovalStore` (`TryGetApprovalAsync`/`RecordApprovalAsync`, mirrors `IIdempotencyKeyStore`'s bridge shape) | (consuming service's own approvals store) | N/A — no cross-domain reference at all, purely a new local seam for a new local-only concern |
| `AuditingBehavior` (WO-071, `○` design-locked, root P-458) | `IAuditTrailWriter` (`RecordAsync(AuditEntry, ct) → Task` — SAME NAME as the real contract, deliberately smaller: no `Id`/`ActorId`/`TenantId`/`OccurredOn`/`CorrelationId`/hash-chain fields, all resolved by the real writer) | `IAuditTrailWriter`/`AuditEntry` | `06.Persistence.Abstractions` (P-456, itself design-locked/implementation-pending as of this dispatch) |

**Why this matters when planning a new phase:** if a new cross-cutting behavior in this domain seems to need something from a higher layer (06/07/12), the answer is almost always "add another local seam, bridged the same way" — not "relax the layering rule" and not "invent a different bridging mechanism." Check this table first before designing something novel.

**Notable exception discovered at WO-071:** unlike every prior local-seam addition, `AuditingBehavior`'s Core phase carries **no genuine cross-domain blocker**, even though the real `06.Persistence` implementation (P-456/P-457) is itself only design-locked, not shipped. The local seam is fully self-contained — this domain's own Core phase never needs the real implementation to exist, only the eventual composition-root bridge (a consuming-service concern) does. Don't assume every local-seam phase is blocked on its "real" counterpart shipping first — check whether the local contract genuinely needs anything from the real one before recording a blocker.

**Same-name-different-namespace naming is the default, not the exception.** `IUnitOfWork` set this precedent (same name in both `05.Application.Behaviors` and `06.Persistence.Abstractions`); `IAuditTrailWriter` (WO-071) followed it deliberately rather than inventing a differentiated name — the local seam's own XML docs and this domain's CLAUDE.md must be explicit about which fields the local shape omits relative to the real one, since the name alone won't signal the size difference.

**Extending an ALREADY-PUBLISHED local seam without a breaking change (WO-058 precedent):** when a new behavior needs one more capability from an existing seam (e.g. `DualApprovalBehavior` needing "who is calling right now" from the same identity source `AuthorizationBehavior` uses), do NOT add a new member directly to the already-shipped interface (`IAuthorizationContext` here) — that breaks every downstream implementation. Instead add a NEW, separate, optional-capability interface (`IAuthorizationContextIdentity`) that an implementation MAY additionally implement, detected via an `is`-pattern-match at the call site — never reflection. This is the exact same technique `IIdempotencyResponseStore` used alongside the already-shipped `IIdempotencyKeyStore` (WO-039, P-242) to add opt-in response replay without breaking existing stores. Two instances of this sibling-capability-interface technique now exist; reach for it by default whenever a new phase wants to grow an already-published seam rather than touching the seam itself.

**`Build()`-time guard convention:** each of these three (plus Caching's `ICacheService`) gets a missing-dependency check inside `ApplicationBehaviorsBuilder.Build()` that throws `InvalidOperationException` with an actionable message if the corresponding seam/abstraction wasn't registered in `IServiceCollection` before `.AddXBehavior()` + `.Build()` ran. This guard convention extends to ANY opt-in behavior with an external dependency, not just layering-seam bridges — WO-036's `ResilienceBehavior` (needs a registered Polly v8 `ResiliencePipelineProvider`) and `CacheInvalidationBehavior` (reuses the EXISTING `ICacheService` guard rather than duplicating it — the guard is keyed on the dependency type, not on which `.AddXBehavior()` call requested it) both follow this same guard shape even though Resilience isn't bridging a higher layer at all (Polly is a same-layer, in-process library, not 06/07/12 infrastructure).

**Important distinction (WO-036):** not every new opt-in behavior needs a NEW local-seam interface. `IRetryableRequest`/`IInvalidatesCache` are markers (zero/near-zero members the request itself implements), not seams (no external-system-bridging interface needed) — `ResilienceBehavior` depends directly on Polly's own `ResiliencePipeline` type (a library type, not an infrastructure layer this domain can't reference), and `CacheInvalidationBehavior` depends on `ICacheService` (already a legitimate `02.Caching.Abstractions` reference `CachingBehavior` established). Don't reflexively invent a new local seam for every new behavior — check first whether the dependency is actually a forbidden-layer reference (06/07/12) or just an already-permitted package/already-pinned library.

See [[project_wo035_seven_behavior_pipeline]] and [[project_wo036_ten_step_pipeline]] for the specific phases these were locked in.

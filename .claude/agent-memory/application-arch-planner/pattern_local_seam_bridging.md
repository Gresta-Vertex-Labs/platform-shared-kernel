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

Three instances of this pattern exist (as of WO-035, 2026-06-29):

| Behavior | Local seam | Bridges to | Real package |
| --- | --- | --- | --- |
| `TransactionBehavior` | `IUnitOfWork` (`SaveChangesAsync` only) | `IUnitOfWork` | `06.Persistence.Abstractions` |
| `AuthorizationBehavior` | `IAuthorizationContext` (`IsAuthorizedAsync(string, ct)` only) | `IUserContext`/`ITenantProvider` | `12.Security.Abstractions` |
| `IdempotentCommandBehavior` | `IIdempotencyKeyStore` (`HasProcessedAsync`/`MarkProcessedAsync`, mirrors shape but not identity of `07.Messaging`'s `IIdempotencyStore`) | (consuming service's own store) | `07.Messaging.Abstractions` (shape-only precedent, not a reference) |

**Why this matters when planning a new phase:** if a new cross-cutting behavior in this domain seems to need something from a higher layer (06/07/12), the answer is almost always "add a fourth local seam, bridged the same way" — not "relax the layering rule" and not "invent a different bridging mechanism." Check this table first before designing something novel.

**`Build()`-time guard convention:** each of these three (plus Caching's `ICacheService`) gets a missing-dependency check inside `ApplicationBehaviorsBuilder.Build()` that throws `InvalidOperationException` with an actionable message if the corresponding seam/abstraction wasn't registered in `IServiceCollection` before `.AddXBehavior()` + `.Build()` ran. This guard convention extends to ANY opt-in behavior with an external dependency, not just layering-seam bridges — WO-036's `ResilienceBehavior` (needs a registered Polly v8 `ResiliencePipelineProvider`) and `CacheInvalidationBehavior` (reuses the EXISTING `ICacheService` guard rather than duplicating it — the guard is keyed on the dependency type, not on which `.AddXBehavior()` call requested it) both follow this same guard shape even though Resilience isn't bridging a higher layer at all (Polly is a same-layer, in-process library, not 06/07/12 infrastructure).

**Important distinction (WO-036):** not every new opt-in behavior needs a NEW local-seam interface. `IRetryableRequest`/`IInvalidatesCache` are markers (zero/near-zero members the request itself implements), not seams (no external-system-bridging interface needed) — `ResilienceBehavior` depends directly on Polly's own `ResiliencePipeline` type (a library type, not an infrastructure layer this domain can't reference), and `CacheInvalidationBehavior` depends on `ICacheService` (already a legitimate `02.Caching.Abstractions` reference `CachingBehavior` established). Don't reflexively invent a new local seam for every new behavior — check first whether the dependency is actually a forbidden-layer reference (06/07/12) or just an already-permitted package/already-pinned library.

See [[project_wo035_seven_behavior_pipeline]] and [[project_wo036_ten_step_pipeline]] for the specific phases these were locked in.

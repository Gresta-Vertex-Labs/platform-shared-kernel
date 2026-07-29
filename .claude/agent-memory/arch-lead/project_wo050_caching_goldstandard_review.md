---
name: project_wo050_caching_goldstandard_review
description: WO-050 02.Caching gold-standard review — stale consumer-verify, missing readmes, unproven cross-pod tag invalidation, sequential batch ops, tracing gap; HybridCache/RedLock.net declines
type: project
---

WO-050 (2026-07-29): direct user request to analyze `02.Caching` (37 phases, 7 packages, the platform's most heavily consumed capability) for gold-standard completeness, developer-friendliness, and currency. Dispatched six phases (P-301–P-306, all `○` Pending) to root `state-map.md`. No `state-map-phase` calls — 02.Caching/13.ServiceDefaults/16.Testing were all already `●` Published on the Domain Summary Board.

**Why:** Even a domain with the most phases in the platform (37) and a mature-looking `CLAUDE.md` can have rotted proof-of-correctness artifacts that nothing catches because nothing re-runs them. This is the second confirmation (after [[feedback_verify_shipped_code_not_docs]]) that a "Published"/`●` domain summary line is not sufficient evidence of current correctness — always read the actual shipped files for a gold-standard review, never trust the domain brain's own narrative alone.

**Concrete findings (verified via a dedicated research subagent reading real `.cs`/`.csproj` files):**
- `02.Caching/consumer-verify/SharedKernel.Caching.ConsumerVerify` imports namespaces retired at the Phase-14 rename (`SharedKernel.Caching` → `.FusionCache`) and references a dead `SharedKernel.Caching` v1.0.0 PackageId — does not compile against current source. Rotted silently across 23 subsequent phases including the entire WO-023 5-way Redis package split. The domain's own `SK.02.Published` phase (marked `●`) only ever covered the original 2-package shape and was never re-run for the 5 packages added/renamed since.
- 5 of 7 packages (`.Abstractions`, `.Redis.Core`, `.DistributedLocking`, `.HashStore`, `.PubSub`) ship with zero `README.md`/`PackageReadmeFile`.
- `RemoveByTagAsync`'s only Redis-backed test constructs a single shared `ServiceProvider` — proves same-instance tag removal only, never cross-pod L2-backplane-propagated invalidation (a heavily-relied-upon multi-pod coherence guarantee).
- `GetManyAsync`/`SetManyAsync` are plain sequential `foreach`-with-`await` loops, not `Task.WhenAll`/pipelined — despite an internal `IRedisL2BatchService` built in Phase 22 seemingly for exactly this purpose. Confirmed via direct source read, not assumption.
- `WithCachingTelemetry` (`13.ServiceDefaults`) wires only a `Meter`, no `ActivitySource` — the only sibling in the `WithXTelemetry` family (Messaging/Application/Search/Intelligence/Workflows all wire both) missing distributed-tracing spans, because it was implemented first (P-010/WO-003) before that convention existed.

**Declined (not written as phases, recorded as architectural decisions):**
- Adopting `Microsoft.Extensions.Caching.Hybrid` (`HybridCache`) instead of/alongside FusionCache — FusionCache 2.6.0 confirmed still actively maintained (current as of March 2026 per NuGet) with a materially richer feature set (fail-safe, adaptive/eager refresh, tagging, multi-named-cache); the platform's own `ICacheService` abstraction already gives the provider-decoupling benefit HybridCache would add.
- Replacing `RedLock.net` for staleness alone — last NuGet release 2.3.2, April 2022 (4+ years stale as of this review, confirmed via WebSearch). Unlike `SharedKernel.AI.Milvus` (WO-048, retracted — `Milvus.Client` never shipped a stable release and blocked scaffolding), RedLock.net has a stable, fully shipped, 41-test-covered production integration with no blocking defect. **Pattern for future reviews:** staleness alone does not justify replacing working, tested code — only retract/replace when there's an actual blocker (no stable release, blocks scaffolding, known defect), not merely "no recent commits." Record as a monitored risk instead.

See also [[project_wo049_core_goldstandard_review]] and [[project_wo048_milvus_retraction]] for the precedent this review's methodology and retract-vs-monitor judgment call followed.

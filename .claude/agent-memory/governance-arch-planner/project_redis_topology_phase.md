---
name: project-redis-topology-phase
description: Redis Package Topology Architecture Rules phase (SK.00.RedisTopology) — design decisions, RedisTopologyRules shape, no new SK IDs
metadata:
  type: project
---

> WO-086 (2026-09): `RedisTopologyRules` still exists (now also `CachingAbstractionsReferencesOnlyDependencyInjectionAbstractions`, `CachingAbstractionsDeclaresNoProviderSpecificTypes`, `DistributedLockingNeverReferencesRedLock`); `CachingAbstractionRules`, cited below as the reference implementation, was deleted in P-574. The four Redis capability packages → `Caching.Redis.Core` are now declared Adapter→Adapter edges (`SharedKernelAllowedAdapterReferences`); the siblings-never-reference-each-other and Messaging ↛ Caching rules remain ArchitectureTests purity rules.

Phase SK.00.RedisTopology was added on 2026-06-12 under WO-023 P-145.

**Why:** P-140–P-144 split the old `SharedKernel.Caching.Redis` package into five packages
(`Redis.Core`, `Redis` (L2), `Redis.DistributedLocking`, `Redis.HashStore`, `Redis.PubSub`).
The careful separation of concerns only holds if it is mechanically enforced — otherwise a
developer will take a shortcut (e.g., `Redis.DistributedLocking` → `Redis.HashStore` "because
it's already a transitive dependency anyway"), or `07.Messaging` and `02.Caching` will drift
back together.

**Design decisions:**

- No new SK diagnostic IDs were assigned. All five checks are pure NetArchTest
  `ConditionList` assembly-dependency-graph predicates (`.Should().NotHaveDependencyOn(...)`),
  the same category as `CachingAbstractionRules.OnlyAllowedAssembliesMayReferenceConcreteCaching`.
  No Roslyn analyzer, no Mono.Cecil, no `ICustomRule` — confirms next available SK ID is
  still SK0012 (sequential), SK0203 (multi-tenancy), SK0305 (encryption), SK0709 (messaging) —
  see [[sk-diagnostic-registry]].
- New static class `RedisTopologyRules` in `SharedKernel.ArchitectureTests/Rules/`, sibling to
  `CachingAbstractionRules.cs`. Five factory methods:
  1. `RedisCoreNeverReferencesCapabilityPackages(Assembly)` — Redis.Core must not reference any
     of the four capability packages (iterative `.Should().NotHaveDependencyOn(term)`, 4 terms).
  2. `CapabilityPackagesNeverReferenceEachOther(params Assembly[])` — pairwise check across the
     4 capability packages; excludes `Redis.Core` and `Caching.Abstractions` from the forbidden
     set (both are permitted deps).
  3. `PubSubNeverReferencesMessaging(Assembly)` — single check, `Redis.PubSub` must not
     reference `SharedKernel.Messaging` (prefix).
  4. `MessagingNeverReferencesCaching(params Assembly[])` — directional converse of #3;
     `SharedKernel.Messaging.*` must not reference `SharedKernel.Caching` (prefix). Both #3
     and #4 are required since NetArchTest dependency checks are directional.
  5. `CachingAbstractionsHasNoInfrastructureDependencies(Assembly)` — re-verification (not new)
     that `SharedKernel.Caching.Abstractions` stays dependency-free against the new 5-package
     set plus StackExchange.Redis/EF Core/MassTransit.
- Permitted cross-reference exemption list (documented in CLAUDE.md under RedisTopologyRules):
  Redis.Core → Caching.Abstractions; all 4 capability packages → Redis.Core; all 4 capability
  packages → Caching.Abstractions. Any other cross-reference must be added to this list before
  use.
- Task numbering: D-48, C-60–C-64 (5 core tasks, one per factory method), T-104–T-112 (9 test
  tasks — 2 fire/pass pairs per rule 1–4, 1 pass-only for rule 5 re-verification), DO-20.
  Total tasks 266 → 282.

**How to apply:** When implementing C-60–C-64, the existing `CachingAbstractionRules.cs` is
the reference implementation for the `ConditionList` multi-assembly combination pattern
(see its "Build a combined ConditionList across all non-exempt assemblies" comment block —
NetArchTest does not support multi-assembly scanning in one call, so the established
workaround returns/combines per-assembly `ConditionList`s). Resolve the exact return shape
(single `ConditionList` vs `ConditionList[]`) for rules 2 and 4 during D-48 before C-61/C-63.

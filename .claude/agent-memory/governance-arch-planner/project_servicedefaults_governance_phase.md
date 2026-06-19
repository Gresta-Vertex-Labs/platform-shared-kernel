---
name: project_servicedefaults_governance_phase
description: SK.00.ServiceDefaultsGovernance (WO-027 P-173) design decisions — health check tag integrity IL-literal-collection technique, composition-root exclusivity extension to Persistence/Messaging/Security providers
metadata:
  type: project
---

WO-027 P-173 added phase `SK.00.ServiceDefaultsGovernance` (17 tasks: D-52, C-71–C-74, T-129–T-138, DO-24; total governance tasks now 328) to `00.Governance/state-map.md`, with corresponding additions to `00.Governance/CLAUDE.md`. Depends on P-170 (13.ServiceDefaults Core) for **real-assembly verification only** — design/implementation proceeds now against contrived in-memory fixtures (same `CSharpCompilation` + `MetadataReference.CreateFromImage` technique as `RedisTopologyRulesTests`), since `SharedKernel.ServiceDefaults`/`SharedKernel.MultiTenancy` don't exist yet.

**Why this phase exists:** `13.ServiceDefaults/CLAUDE.md` claims two things are "mechanically enforced by SharedKernelLayeringRules" that were not actually true: (1) live/ready tag mutual exclusivity, (2) composition-root exclusivity for ALL provider families. Rule (2) was only ever scoped to `02.Caching` (P-009/WO-003, written before `13.ServiceDefaults` existed). This is the same category of aspirational-but-unenforced-rule gap previously closed for Redis topology in P-145 — see [[project_redis_topology_phase]].

**Design decisions:**

1. **No new SK Roslyn diagnostic IDs.** Both rule groups are NetArchTest `ICustomRule`/`ConditionList` predicates, assembly-level post-compile checks — consistent with `RedisTopologyRules`, `CachingAbstractionRules`, `ReflectionGuardRules`. Next available sequential-block SK ID remains **SK0014** (not assigned this phase) — see [[project_sk_diagnostic_registry]].

2. **`HealthCheckTagIntegrityRules`** — introduces a NEW Mono.Cecil technique for this domain: **IL `Ldstr` literal-collection** (collecting a *set* of string literal operands within a scoped method and reasoning about set membership), distinct from every prior predicate's **opcode-presence** technique (single opcode/name match, e.g. `NoMakeGenericMethodReflectionPredicate`). Two predicates:
   - `NoConflictingLivenessReadinessTagsPredicate` — fails if a single registration's literal tag set contains both `"live"` and `"ready"`.
   - `DependencyHealthChecksCarryReadyNotLivePredicate` — constructed with caller-supplied method-name-prefix list (e.g. `"AddRedis"`, `"AddDatabase"`) rather than hardcoding extension names that don't exist yet (P-170 not shipped); fails if `"ready"` absent or `"live"` present.
   - Both scoped by `MethodDefinition.Name` prefix/suffix matching to `SharedKernel.ServiceDefaults`'s own `Add*HealthCheck`/`Add*ReadinessCheck` extension methods — NOT arbitrary call sites across any assembly. This is the platform's sole sanctioned health-check registration surface.
   - **Documented limitation (not a defect):** this is literal-collection, NOT full data-flow analysis. A tag computed dynamically (e.g. from `IConfiguration`) is invisible to both predicates — advisory-only for that call site, must be caught in code review.

3. **`CompositionRootExclusivityRules.OnlyAllowedAssembliesMayReferenceConcreteProviders(params Assembly[])`** — pure NetArchTest, zero Mono.Cecil, structural mirror of `CachingAbstractionRules.OnlyAllowedAssembliesMayReferenceConcreteCaching`. Returns `ConditionList[]`, one per forbidden term (5 total): `SharedKernel.Persistence.EfCore`, `.PostgreSQL`, `.Dapper`, `SharedKernel.Messaging.MassTransit`, `SharedKernel.Security.Oidc`. Composition-root exemption list: `SharedKernel.ServiceDefaults`, `SharedKernel.MultiTenancy`, and each provider package referencing itself. Caller must NOT pass any of these exempt assemblies (same self-reference-exclusion discipline as `RedisTopologyRules.CapabilityPackagesNeverReferenceEachOther`).

**Follow-up tracked (not blocking phase completion):** once P-170 ships the real `SharedKernel.ServiceDefaults` assembly, re-run both rule groups against it (not just fixtures) and record the confirmation in a new Changelog entry — same precedent as how `RedisTopologyRules` was fixture-verified then later confirmed (P-145 closeout, 2026-06-15).

Last D/C/T/DO IDs before this phase: D-51, C-70, T-128, DO-23. After this phase: D-52, C-74, T-138, DO-24.

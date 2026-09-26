---
name: project-storage-topology-phase
description: Storage Package Topology Architecture Rules phase (SK.00.StorageTopology, WO-043 P-271) — SK0023 design, StorageTopologyRules shape, pre-implementation-dependency status
metadata:
  type: project
---

> WO-086 (2026-09): historical design. Today `StorageTopologyRules` holds `AbstractionsHasNoThirdPartyDependencies`, `AbstractionsForbiddenAssemblyReferences`, `S3NeverReferencesObs`, `S3ForbiddenAssemblyReferences` and `OnlyProviderPackagesMayReferenceAmazonS3` (Obs→S3 is a declared adapter edge). `CompositionRootExclusivityRules` was deleted in P-574, and `UnitOfWorkSeamRules` now holds only `SharedContractsAreNotRedeclared` — use the code, not the reference implementations named below.

Phase SK.00.StorageTopology was added on 2026-07-16 under WO-043 P-271, dispatched alongside
the rest of `08.Storage`'s first build-out (P-265–P-271). This is the first phase where
`00.Governance` designed enforcement for a domain that had **zero** buildable assemblies at
design time — `08.Storage/state-map.md` showed every phase (Design through Published) at
`○`/empty, and even P-265 (the abstraction-contract finalization the whole build-out depends
on) was only `◐` (Design in progress) in the root state-map.

**Why:** `08.Storage/CLAUDE.md` was written by arch-lead as a target-state spec ahead of any
code — it already documents, in prose, that `SharedKernel.Storage.Abstractions` has zero
third-party NuGet dependencies, that `.S3`/`.Obs` are sibling packages that must never
reference each other, that application code must never touch `Amazon.S3.*` directly, and that
`IAmazonS3` must be a singleton. Per the platform's now-repeated lesson (raw `HttpClient`
P-159, inline `ProblemDetails` P-199, ad hoc logging P-250, magic strings P-264, and the direct
precedent [[project_redis_topology_phase]] P-145), a new domain should ship its enforcement
suite alongside its first implementation, not retrofitted after a violation ships.

**Design decisions:**

- **SK0023 `NonSingletonAmazonS3ClientRegistration`** — the platform's first storage-domain
  Roslyn diagnostic, and the structural INVERSE of SK0703 (`MessageBusSingletonRegistration`):
  SK0703 flags `AddSingleton<IMessageBus>` because that type must be *scoped*; SK0023 flags
  `AddScoped<IAmazonS3>`/`AddTransient<IAmazonS3>` because that type must be *singleton*. Reuses
  SK0703's exact type-argument-extraction technique (`GenericNameSyntax.TypeArgumentList
  .Arguments[0]` as `IdentifierNameSyntax`, simple-name match). Syntax-only, no SemanticModel.
  Fires globally, no suppression namespace — mirrors SK0703/SK0014's "fires globally"
  convention. Confirms next available sequential ID is now **SK0024** — see
  [[project_sk_diagnostic_registry]].
- **No new `08xx` ID block was opened.** A single narrow storage-domain rule doesn't warrant
  its own block (the `02xx`/`03xx`/`07xx` blocks exist for multi-rule subsystem families —
  multi-tenancy, encryption, messaging — each with several related rules). SK0023 follows the
  SK0011 (persistence)/SK0013 (communication) precedent: a lone domain-specific rule stays in
  the sequential general-purpose block.
- **`StorageTopologyRules`** (new static class in `SharedKernel.ArchitectureTests/Rules/`) —
  mirrors `RedisTopologyRules` exactly but scoped to 08.Storage's TWO provider packages instead
  of Redis's five, so it needed no `Dictionary<string,string[]>` self-term-resolution lookup
  table:
  1. `AbstractionsHasNoThirdPartyDependencies(Assembly)` — four iterative
     `NotHaveDependencyOn` terms: `"Amazon"` (bare prefix, catches all of AWSSDK.S3),
     `"SharedKernel.Storage.S3"`, `"SharedKernel.Storage.Obs"`, `"SharedKernel.Configuration"`.
  2. `ProviderPackagesNeverReferenceEachOther(Assembly s3Assembly, Assembly obsAssembly)` →
     `ConditionList[]` (2 elements) — TWO NAMED `Assembly` parameters, not `params Assembly[]`,
     mirroring `UnitOfWorkSeamRules.UnitOfWorkInterfacesRemainDistinct`'s two-named-parameter
     convention (comparing two specific named packages; positional params would obscure which
     is which).
  3. `OnlyProviderPackagesMayReferenceAmazonS3(params Assembly[])` — single
     `NotHaveDependencyOn("Amazon.S3")` check; caller-controlled exclusion (never pass
     `.S3`/`.Obs` themselves), mirroring `PresentationLayeringRules`/
     `CompositionRootExclusivityRules`'s caller-controlled-exclusion convention rather than an
     internal namespace guard, since no single internal namespace prefix distinguishes
     "legitimate AWSSDK.S3 usage" from "leaked usage" other than which package the type is in.
- Zero new Mono.Cecil technique, zero new `ICustomRule`, zero new NuGet dependency for either
  the analyzer (`Microsoft.CodeAnalysis.CSharp` 4.14.0) or the rule (`NetArchTest.eNt` >= 1.3.2).
- Task numbering: D-62, C-103–C-106 (4 core tasks: 1 analyzer + 3 factory methods), T-191–T-199
  (9 test tasks: 3 analyzer fire/fire/pass + 3 rule fire/pass pairs), DO-34. Total tasks
  426 → 441.
- **UNVERIFIABLE against real assemblies at authoring time (2026-07-16)** — `08.Storage` has
  not shipped any of P-265/P-266/P-267 yet. Design/tests use CONTRIVED in-memory assemblies via
  `CSharpCompilation` + `MetadataReference.CreateFromImage` (the `RedisTopologyRulesTests`/
  `CompositionRootExclusivityRulesTests` technique). A follow-up confirmation pass against the
  real `SharedKernel.Storage.S3`/`.Obs` assemblies is required once P-266/P-267 land — tracked
  as a Cross-Domain Dependency row in `00.Governance/state-map.md`, NOT a blocking condition on
  this phase's own completion. This is now the FOURTH "designed-ahead-of-a-pending-dependency"
  phase in this domain (after `SK.00.ServiceDefaultsGovernance`,
  `SK.00.MetricsOutcomeTagAndMisregistrationGuard`/`SK.00.CryptoDelegationAndUowSeamGuard`, and
  `SK.00.MagicStringGuard`) — but the first one where the DEPENDENT domain itself had not even
  started implementation (P-259/P-249/P-217 all had at least a design locked; P-265 here is
  merely `◐` in-progress design).

**How to apply:** When implementing C-103–C-106, `RedisTopologyRules.cs` and
`CompositionRootExclusivityRules.cs` are the reference implementations for the
`NotHaveDependencyOn` iterative-term and caller-controlled-exclusion patterns respectively.
`UnitOfWorkSeamRules.UnitOfWorkInterfacesRemainDistinct` is the reference for the two-named-
`Assembly`-parameter method shape. Do NOT attempt to point any `StorageTopologyRules` factory
method at a real `SharedKernel.Storage.*` assembly until `08.Storage` P-266/P-267 both ship —
check `08.Storage/state-map.md`'s Package Board first.

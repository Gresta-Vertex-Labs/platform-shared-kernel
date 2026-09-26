---
name: project_application_pipeline_arch_rules_phase
description: SK.00.ApplicationPipelineArchRules (WO-036 P-225) design decisions — fourth Mono.Cecil technique (generic-constraint inspection), Task.Delay fingerprint heuristic, first non-ConditionList helper (PipelineOrderAssertion)
metadata:
  type: project
---

> WO-086 (2026-09): `SharedKernel.Application.Behaviors` is now `SharedKernel.Application.Pipeline` (Host tier, kernel-owned `IPipelineBehavior<,>`, no MediatR — MediatR is referenced only by `SharedKernel.Application.Mediator.MediatR`, locked by `DependencyGraphRulesTests.MediatR_IsReferencedOnlyByTheMediatorAdapter`). `ApplicationPipelineRules` today holds `BehaviorsNeverReferenceConcreteInfrastructure`, `NoExistingBehaviorMatchesStreamRequestConstraint`, `PipelineNeverReferencesCachingPollyHostingOrCore` and `PipelineCachingNeverReferencesConcreteInfrastructure` — grep before naming a rule. "Layering: 00.Governance references nothing" now reads: governance packages are Tooling tier. Also gone: `NoHandRolledRetryLoopOutsideResilienceBehavior` (`ResilienceBehavior` was removed, P-544), `CompositionRootExclusivityRules`/`CachingAbstractionRules` (P-574, tier check) and `SagaStateMustExtendSagaStateBasePredicate` (sagas removed from Messaging, P-560). `PipelineOrderAssertion` still exists.

WO-036 P-225 added phase `SK.00.ApplicationPipelineArchRules` (13 tasks: D-55, C-80–C-84,
T-147–T-153, DO-27; total governance tasks now 359) to `00.Governance/state-map.md`, with
corresponding additions to `00.Governance/CLAUDE.md`. Depends on `05.Application` P-220
(`TracingBehavior`), P-221 (streaming vocabulary), P-222 (`ResilienceBehavior`), P-224
(`CacheInvalidationBehavior`) for **real-assembly verification only** — WO-036 is design-only
as of 2026-06-30 (`05.Application/state-map.md` Core phase C-18..C-29 not started), so design
proceeds against contrived in-memory fixtures, same technique as
[[project_servicedefaults_governance_phase]] and the `SK.00.PresentationArchRules`/
`SK.00.HealthCheckConstantsGuard` precedent.

**Why this phase exists:** every prior `05.Application` work order (WO-035) paired new pipeline
behaviors with a governance phase because documented-but-unenforced rules decay under time
pressure — same rationale as every prior phase in this file. WO-036 adds three behaviors and a
new request shape (streaming) to a domain whose seven-step (soon ten-step) pipeline already had
zero margin for accidental reordering.

**Design decisions:**

1. **No new SK diagnostic IDs.** All three new checks are Mono.Cecil `ICustomRule` predicates —
   consistent with `RedisTopologyRules`, `CompositionRootExclusivityRules`,
   `GrpcNeverReferencesContracts`, `PresentationLayeringRules`. Next available sequential-block
   SK ID remains **SK0014** — see [[project_sk_diagnostic_registry]].

2. **`ApplicationPipelineRules`** (new static class, `Rules/ApplicationPipelineRules.cs`) — three
   factory methods:
   - `BehaviorsNeverReferenceConcreteInfrastructure(params Assembly[])` — caller-supplied
     `HashSet<string>` of behavior simple names (`TracingBehavior`, `ResilienceBehavior`,
     `CacheInvalidationBehavior`) + caller-supplied forbidden-namespace set, never hardcoded
     inside the predicate (mirrors `HealthCheckTagIntegrityRules`'s caller-supplied-prefix
     convention). Excludes `.Abstractions` sub-namespaces explicitly.
   - `NoExistingBehaviorMatchesStreamRequestConstraint(Assembly)` — introduces this domain's
     **fourth distinct Mono.Cecil technique**: IL **generic-parameter-constraint inspection**
     (`GenericParameter.Constraints` on the `TRequest` type parameter of `IPipelineBehavior<,>`
     implementors, with interface-closure resolution), distinct from opcode-presence
     (`NoMakeGenericMethodReflectionPredicate`), `Ldstr` literal-collection
     (`HealthCheckTagIntegrityRules`), and field-shape/literal-value resolution
     (`StringConstantsClassDetector`). A structural check, not a runtime DI test — fails at the
     architecture-test stage before any wiring attempt. Fail-open on unresolved
     `TypeReference.Resolve()`, same policy as `SagaStateMustExtendSagaStateBasePredicate`.
   - `NoHandRolledRetryLoopOutsideResilienceBehavior(Assembly)` — `Task.Delay` Call/Callvirt
     fingerprint (`MethodReference.Name == "Delay"` AND `DeclaringType.FullName ==
     "System.Threading.Tasks.Task"`), self-exempts only the exact type name
     `ResilienceBehavior`. Explicitly documented as a **fingerprint heuristic, not a full
     retry-loop detector** — accepts the false-positive risk of a legitimate non-retry
     `Task.Delay` call (none known to exist today). Extends the existing
     `System.Random`/`DateTime.UtcNow` hand-rolled-primitive prohibition pattern to retry/backoff.

3. **`PipelineOrderAssertion`** — the **first artifact in `SharedKernel.ArchitectureTests` that
   is not a `ConditionList`/`ICustomRule`**. A plain public reflection helper:
   `AssertRegistrationOrder(IServiceCollection services, params Type[] expectedBehaviorTypesInOrder)`.
   Walks `ServiceDescriptor` entries for the open generic `IPipelineBehavior<,>` directly off an
   **unbuilt** `IServiceCollection` (deliberately never calls `BuildServiceProvider()`) and
   asserts the closed-generic implementation sequence matches expectation. Ships here (not
   `16.Testing`) because it asserts an *architectural* invariant (fixed pipeline composition
   order), the same rationale that already places `ArchitectureRuleBase` here. Intended consumer:
   `05.Application.Behaviors.Tests` (WO-036 T-17/T-18), proving `ApplicationBehaviorsBuilder
   .Build()`'s ten-named-slot order can't silently drift. Its own correctness (pass/fail cases)
   is proven by a governance-owned unit test (T-153), separate from `05.Application`'s eventual
   real-assembly consumption.

4. **Cross-domain consumption pattern reaffirmed**: `00.Governance` never references
   `05.Application`/`05.Application.Behaviors` (layering: `00.Governance` references nothing).
   All four artifacts are designed/tested here against contrived fixtures; `05.Application` is
   responsible for invoking them against its own real assembly once WO-036's Core phase ships —
   same pattern as `CachingAbstractionRules`/`RedisTopologyRules` (consumed by `02.Caching`) and
   `PersistenceLayerProtectionRules` (consumed by `06.Persistence`).

**How to apply:** When WO-036's Core phase lands in `05.Application`, the implementer should (a)
pass the real `SharedKernel.Application.Behaviors` assembly to all three
`ApplicationPipelineRules` factory methods for real-assembly re-verification (tracked as a
follow-up, same as the P-170 ServiceDefaults precedent), and (b) wire `PipelineOrderAssertion`
into the reusable pipeline test harness (C-29) rather than hand-rolling reflection-based order
verification inside `05.Application.Behaviors.Tests` itself.

Last D/C/T/DO IDs before this phase: D-54, C-79, T-146, DO-26. After this phase: D-55, C-84,
T-153, DO-27. Total governance tasks: 346 → 359.

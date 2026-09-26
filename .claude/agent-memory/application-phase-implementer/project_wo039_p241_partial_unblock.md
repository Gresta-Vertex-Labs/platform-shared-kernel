---
name: project-wo039-p241-partial-unblock
description: WO-039 P-241 gap-closure — which real-assembly architecture rules are independent of the 00.Governance P-240 reflection-exemption blocker
metadata:
  type: project
---

> WO-086 (2026-09): historical. MediatRDomainEventDispatcher is now DomainEventDispatcher (SharedKernel.Application.Pipeline), SharedKernel.Application.Behaviors is SharedKernel.Application.Pipeline, and ResilienceBehavior was deleted. The "verify every sub-part of a blocker" lesson still holds.

WO-039 P-241 (05.Application/state-map.md, SK.05.Tests phase) requires wiring four already-built
`00.Governance/SharedKernel.ArchitectureTests` rule groups against the REAL `SharedKernel.Application`/
`SharedKernel.Application.Behaviors` assemblies. For several sessions, all five tasks (T-40..T-44) were
left `○` under the assumption they were all blocked on `00.Governance` P-240 (the
`ReflectionExemptionRegistry` entry for `MediatRDomainEventDispatcher`, tracked in
`00.Governance/state-map.md`'s `SK.00.DomainEventDispatcherReflectionExemption` phase).

**That assumption was only true for one of the four rule groups.** On 2026-07-06 (third session), reading
the actual implementations directly proved three are structurally unrelated to P-240:

- `ApplicationPipelineRules.BehaviorsNeverReferenceConcreteInfrastructure` / `.NoExistingBehaviorMatchesStreamRequestConstraint` / `.NoHandRolledRetryLoopOutsideResilienceBehavior` — pure NetArchTest `ICustomRule` predicates over namespace references / generic constraints / `Task.Delay` call sites. Zero mention of `MakeGenericMethod` or the exemption registry.
- `PipelineOrderAssertion.AssertRegistrationOrder` — a plain reflection helper over an unbuilt `IServiceCollection`'s `ServiceDescriptor` list. Not a NetArchTest rule at all, no exemption-registry dependency.
- `MetricsInstrumentationRules.RequestDurationRecordsIncludeOutcomeTag` — depends only on P-239 (outcome tag) having shipped in `MetricsBehavior<,>`, which it had.
- Only `ReflectionGuardRules.NoMakeGenericMethodReflection` actually needs the `ReflectionExemptionRegistry` entry — because it scans for `MakeGenericMethod` call sites, and `MediatRDomainEventDispatcher` (and now also `ResultOfTDispatcher<TResponse>`, see `05.Application/CLAUDE.md` "Constructing a generic failure response") has one.

**Why:** prior sessions treated "P-241 is blocked on P-240" as an atomic fact about the whole phase without
re-checking whether every constituent rule group actually depended on it. Blindly re-verifying only
"is P-240 done yet" (still 0/5, unchanged across at least 3 sessions) without re-examining the four rule
groups' actual bodies caused three shippable tasks to sit `○` far longer than necessary.

**How to apply:** When a phase task says "blocked on X," and X is a multi-part blocker or the task itself
bundles several sub-invocations, read the actual code/predicate bodies before accepting the blanket "still
blocked" status inherited from a prior session's note. A blocker note is a claim about the state *at the time
it was written* — verify it still applies to every sub-part, not just the phase as a whole.

Result: T-41/T-42/T-43 shipped via new `Governance/RealAssemblyArchitectureRulesTests.cs` in
`SharedKernel.Application.Behaviors.Tests`. T-40 and T-44 remain genuinely blocked pending
`00.Governance` P-240.

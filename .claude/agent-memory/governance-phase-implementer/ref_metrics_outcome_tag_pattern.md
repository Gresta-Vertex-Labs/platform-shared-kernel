---
name: ref_metrics_outcome_tag_pattern
description: WO-038 P-235 (SK.00.MetricsOutcomeTagAndMisregistrationGuard) — unbound-generic AllInterfaces pitfall for typeof(X<,>) DI registration analyzers, exact GenericInstanceType.ElementType.FullName matching for Histogram<T>.Record predicate
metadata:
  type: reference
---

> WO-086 (2026-09): SK0015 (`StreamPipelineBehaviorMisregistrationAnalyzer`) was deleted and its ID retired — the kernel pipeline has its own `IStreamPipelineBehavior<,>` and no mediator stream registration to get wrong; removal recorded under `### Removed Rules` in `AnalyzerReleases.Unshipped.md`. The unbound-generic `OriginalDefinition` lesson below still applies to any `typeof(Foo<,>)` analyzer. SK0014, SK0016 and `RequestDurationRecordMissingOutcomeTagPredicate` still exist.

## SK0015 (StreamPipelineBehaviorMisregistrationAnalyzer) — unbound generic symbol pitfall

When a Roslyn analyzer resolves the type argument of `typeof(SomeType<,>)` (an unbound/open generic
used in the `services.AddTransient(Type serviceType, Type implementationType)` two-arg overload) via
`SemanticModel.GetTypeInfo(typeSyntax).Type`, the resulting `INamedTypeSymbol.IsUnboundGenericType`
is `true`. **`INamedTypeSymbol.AllInterfaces` returns an EMPTY collection for an unbound generic type
symbol** — confirmed via a standalone Roslyn symbol-inspection scratch script (compiled + ran a tiny
console program dumping `GetTypeInfo` results for `typeof(StreamFixtureBehavior<,>)`).

Fix: call `.OriginalDefinition.AllInterfaces` instead — `OriginalDefinition` on an unbound generic
type resolves to the bound generic type *definition*, whose `AllInterfaces` is populated correctly
(interfaces still expressed in terms of the type's own type parameters, e.g.
`MediatR.IStreamPipelineBehavior<TRequest, TResponse>`).

This is the same "unbound generic member queries return empty" trap that could bite any future
analyzer resolving `typeof(Foo<,>)` symbols — always route interface/base-type walks through
`.OriginalDefinition` when the source symbol came from an omitted-type-argument `typeof()`
expression, not just from a genuinely open type parameter.

## RequestDurationRecordMissingOutcomeTagPredicate — exact FullName match required, not name-prefix heuristic

Initial draft used `declaringType.Name.StartsWith("Histogram", Ordinal)` after unwrapping
`GenericInstanceType.ElementType` — this compiled and passed tests, but the pre-written CLAUDE.md
Architecture Test Contracts spec was more precise: require `MethodReference.DeclaringType` to be a
`GenericInstanceType` **and** its `ElementType.FullName == "System.Diagnostics.Metrics.Histogram\`1"`
exactly (Mono.Cecil's generic-arity-suffixed FullName). Reconciled the implementation to the
documented contract rather than leaving the looser heuristic — this is a case where DO-29-style
verification against pre-written CLAUDE.md caught a real (if currently harmless) divergence between
implementation and spec. Contrived test fixtures define their own local
`namespace System.Diagnostics.Metrics { public sealed class Histogram<T> { ... } }` stub (no BCL
System.Diagnostics.DiagnosticSource reference needed) — Mono.Cecil resolves `FullName` for this local
stub identically to the real BCL type since it's namespace+name based, not assembly-identity based.

## SK0014/SK0016 — no surprises

SK0014 (arity-1 `ResiliencePipeline<T>` syntax match) and SK0016 (trigger-IN namespace scope for
`typeof(X).Name` without `.FullName` companion) both worked exactly as designed on the first pass —
no Roslyn API pitfalls. SK0016's `HasFullNameCoalesceCompanion` check does a syntactic (not semantic)
text-match between the `.FullName` and `.Name` operands' `typeof()` type argument — cheap and correct
since both must reference the same type parameter/type name to count as the sanctioned pattern.

## Outcome

105/105 SharedKernel.Analyzers.Tests, 127/127 SharedKernel.ArchitectureTests.Tests, 0 build
warnings/errors. Phase Key Registry gap recurred again (same as SK.00.ServiceDefaultsGovernance /
SK.00.HealthCheckConstantsGuard, see [[ref_servicedefaults_governance_rules]]) — used the `WO-038
P-235` trigger annotation to resolve the root Phase Backlog ID for closure since no Registry row
existed for this phase key.

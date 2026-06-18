---
name: communication-arch-rules-phase
description: SK.00.CommunicationArchRules design decisions: CommunicationLayeringRules (NetArchTest), SK0013 RawHttpClientConstructorInjection (Roslyn), two ICustomRule predicates, hardcoded-URI guard decision
metadata:
  type: project
---

SK.00.CommunicationArchRules added 2026-06-18 as part of WO-025 P-159. Phase key maps to root backlog P-159. Depends on P-154 (Rest), P-155 (Internal), P-156 (Grpc), P-157 (GraphQL) complete.

## Phase summary

17 tasks total (D-50, C-67–C-69, T-116–T-126, DO-22), all at ○ Pending as of 2026-06-18.

## New static class: CommunicationLayeringRules (SharedKernel.ArchitectureTests)

Four factory methods, all returning ConditionList (or ConditionList[]):

1. `.CommunicationPackagesNeverReferencesForbiddenLayers(Assembly)` — iterative NotHaveDependencyOn for "SharedKernel.Caching", "SharedKernel.Application", "SharedKernel.Persistence", "SharedKernel.Messaging"; returns ConditionList[] (one per term); caller must assert each. Consistent with DomainLayerPurityRules iterative pattern.
2. `.CommunicationInternalNeverReferencesOtherCommunicationPackages(Assembly)` — three terms: "SharedKernel.Communication.Rest", "SharedKernel.Communication.Grpc", "SharedKernel.Communication.GraphQL"; returns ConditionList[]. Caller passes Communication.Internal assembly only.
3. `.NoDirectGrpcInterceptorInheritanceOutsideCommunicationGrpc(Assembly)` — uses NoDirectGrpcInterceptorInheritancePredicate (ICustomRule); returns ConditionList. Caller passes any non-Grpc assembly.
4. `.NoDirectHotChocolateFilterSortInheritanceOutsideGraphQL(Assembly)` — uses NoDirectHotChocolateFilterSortInheritancePredicate (ICustomRule); returns ConditionList. Caller passes any non-GraphQL assembly.

## New ICustomRule predicates (SharedKernel.ArchitectureTests/Predicates/)

### NoDirectGrpcInterceptorInheritancePredicate
- Namespace exemption guard first: TypeDefinition.Namespace.StartsWith("SharedKernel.Communication.Grpc") → pass unconditionally
- For all other types: iterative BaseType chain walk
- At each step: TypeReference.Name == "Interceptor" (exact) AND TypeReference.Namespace contains "Grpc.Core.Interceptors" (substring)
- Fail-open: BaseType.Resolve() returns null → pass (avoids false positives when gRPC transitive deps not fully loaded)
- Reuses SagaStateMustExtendSagaStateBasePredicate chain-walk pattern. No new NuGet.

### NoDirectHotChocolateFilterSortInheritancePredicate
- Namespace exemption guard first: TypeDefinition.Namespace.StartsWith("SharedKernel.Communication.GraphQL") → pass unconditionally
- For all other types: iterative BaseType chain walk
- Forbidden set: TypeReference.Name.StartsWith("FilterInputType") OR TypeReference.Name.StartsWith("SortInputType") — StartsWith handles generic IL names ("FilterInputType`1")
- Platform-wrapper set: TypeReference.Name.StartsWith("FilterBase") OR TypeReference.Name.StartsWith("SortBase")
- Logic: if FilterBase/SortBase encountered BEFORE forbidden type → compliant (pass); if forbidden type encountered without prior FilterBase/SortBase → fail
- Fail-open on null Resolve(). Reuses chain-walk pattern. No new NuGet.

## SK0013 RawHttpClientConstructorInjection (SharedKernel.Analyzers)

- Roslyn DiagnosticAnalyzer, netstandard2.0, syntax-only (no SemanticModel)
- Registers on ConstructorDeclarationSyntax
- Check: any ParameterSyntax.Type is SimpleNameSyntax/IdentifierNameSyntax with Identifier.Text == "HttpClient" (exact match)
- Two exemptions (applied before firing):
  (a) Namespace exemption: SyntaxNode.Parent walk for any NamespaceDeclarationSyntax/FileScopedNamespaceDeclarationSyntax whose Name.ToString().StartsWith("SharedKernel.Communication.Rest") — same parent-walk pattern as SK0001/SK0007/SK0202
  (b) Base class exemption: ClassDeclarationSyntax.BaseList.Types — any type whose simple name is "DelegatingHandler" (exact) — DelegatingHandler subclasses legitimately accept HttpClient in the delegating chain
- Per-constructor suppression via #pragma warning disable SK0013
- Category: Usage, Severity: Warning
- No blanket suppression namespace — global rule

## Hardcoded URI guard — documentation-only decision

The P-159 spec included a hardcoded URI guard (production typed clients must not assign BaseAddress or Address from string literals or IConfiguration values directly). This was explicitly decided as **documentation-only** — no NetArchTest rule or Roslyn analyzer.

**Why:** Detecting URI assignment to HttpClient.BaseAddress or GrpcChannel config requires:
- Semantic model type resolution of the assignment target
- Distinguishing typed-client constructors from general-purpose code

False positive rate across legitimate URI construction patterns (tests, startup helpers, utilities) was judged too high for a platform-wide rule at this time. Decision is recorded in CLAUDE.md implementation rules section and in CommunicationLayeringRules documentation.

**How to apply:** If a test harness for typed-client factories is introduced, revisit this decision. Any future implementation would be a Roslyn analyzer (not NetArchTest) targeting HttpClient property assignment expressions with a semantic model check on the target type.

## Key architectural decisions

1. CommunicationPackagesNeverReferencesForbiddenLayers returns ConditionList[] (plural) — one per forbidden term — NOT a single ConditionList. This is consistent with RedisTopologyRules.CapabilityPackagesNeverReferenceEachOther (also returns ConditionList[]) and is required because NetArchTest's NotHaveDependencyOn only checks one term per call.

2. NoDirectHotChocolateFilterSortInheritancePredicate uses StartsWith pattern for generic IL type names — "FilterInputType`1" in IL must be caught by StartsWith("FilterInputType"), not exact match. Same applies to SortInputType, FilterBase, SortBase.

3. CommunicationLayeringRules introduces ZERO new SK IDs beyond SK0013. The four NetArchTest predicates are assembly-level rules.

**Related:** [[sk-diagnostic-registry]], [[redis-topology-phase]]

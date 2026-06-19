---
name: ref_communication_arch_rules
description: CommunicationLayeringRules patterns, BaseType chain-walk predicates, and test fixture techniques for WO-025 P-159
metadata:
  type: reference
---

## CommunicationLayeringRules overview (WO-025 P-159)

Four factory methods in `SharedKernel.ArchitectureTests/Rules/CommunicationLayeringRules.cs`:

1. `CommunicationPackagesNeverReferencesForbiddenLayers(Assembly)` → `ConditionList[]` (4 elements)
   - Forbidden terms: `"SharedKernel.Caching"`, `"SharedKernel.Application"`, `"SharedKernel.Persistence"`, `"SharedKernel.Messaging"`
   - Same iterative pattern as `DomainLayerPurityRules.DomainAssembliesNeverReferenceInfrastructure`

2. `CommunicationInternalNeverReferencesOtherCommunicationPackages(Assembly)` → `ConditionList[]` (3 elements)
   - Forbidden sibling terms: `"SharedKernel.Communication.Rest"`, `"SharedKernel.Communication.Grpc"`, `"SharedKernel.Communication.GraphQL"`

3. `NoDirectGrpcInterceptorInheritanceOutsideCommunicationGrpc(Assembly)` → `ConditionList`
   - Uses `NoDirectGrpcInterceptorInheritancePredicate` (ICustomRule)
   - Namespace exemption: `SharedKernel.Communication.Grpc`

4. `NoDirectHotChocolateFilterSortInheritanceOutsideGraphQL(Assembly)` → `ConditionList`
   - Uses `NoDirectHotChocolateFilterSortInheritancePredicate` (ICustomRule)
   - Namespace exemption: `SharedKernel.Communication.GraphQL`

All use `Types.InAssembly(assembly).That().HaveNameStartingWith(string.Empty).Should()...` to select all types before the predicate.

## BaseType chain-walk predicate pattern (NoDirectGrpcInterceptorInheritancePredicate)

```csharp
public bool MeetsRule(TypeDefinition type)
{
    // Namespace exemption first
    if (type.Namespace?.StartsWith("SharedKernel.Communication.Grpc", StringComparison.Ordinal) == true)
        return true;

    var baseType = type.BaseType;
    while (baseType is not null && baseType.Name != "Object")
    {
        if (baseType.Name == "Interceptor" &&
            baseType.Namespace?.Contains("Grpc.Core.Interceptors", StringComparison.Ordinal) == true)
            return false; // violation

        var resolved = baseType.Resolve();
        if (resolved is null) return true; // fail-open

        baseType = resolved.BaseType;
    }
    return true;
}
```

## Platform-wrapper-first chain-walk (NoDirectHotChocolateFilterSortInheritancePredicate)

Check for PLATFORM WRAPPER (FilterBase/SortBase) BEFORE forbidden type (FilterInputType/SortInputType):

```csharp
while (baseType is not null && baseType.Name != "Object")
{
    if (IsPlatformWrapper(baseType.Name)) return true;  // compliant
    if (IsForbiddenHotChocolateBase(baseType.Name)) return false;  // violation

    var resolved = baseType.Resolve();
    if (resolved is null) return true; // fail-open
    baseType = resolved.BaseType;
}
```

Use `StartsWith` for generic IL names: `FilterInputType\`1`, `FilterBase\`1`.

## T-119 fixture: avoid System.Uri

When writing in-memory test fixtures for `CommunicationInternalNeverReferencesOtherCommunicationPackages`,
avoid `System.Uri` as a return type — it lives in `System.Private.Uri` which is not included in the basic
`MetadataReference.CreateFromFile(typeof(object).Assembly.Location)` references. Use `string` instead:

```csharp
// WRONG — CS1069 error
public interface IServiceEndpointResolver { System.Uri Resolve(string name); }

// CORRECT
public interface IServiceEndpointResolver { string Resolve(string name); }
```

## State-map task count discrepancy

The Overall Progress table showed 17 tasks for `SK.00.CommunicationArchRules` but the task table
only had 16 rows (D-50, C-67–C-69, T-116–T-126, DO-22 = 16). The count was corrected to 16/16 `●`
during the state-map update. The phase header had "17 tasks" in text but the table was authoritative.

## WO-026 P-167: GrpcNeverReferencesContracts — fifth CommunicationLayeringRules factory method

Added a fifth method to the existing static class (no new class, per phase scope): single
`Types.InAssembly(grpcAssembly).Should().NotHaveDependencyOn("SharedKernel.Contracts")` call,
no ICustomRule, no Mono.Cecil. Locks the P-163 dead-reference removal permanently — no exemption
permitted by design (any future need requires a governance review and explicit CLAUDE.md revision).

Pattern observed: the governance-arch-planner agent had ALREADY written the full CLAUDE.md
documentation for this rule (Architecture Test Contracts entry + WO-026 Governance Conventions
section + changelog line) before the implementer ran. The implementer's job was to verify the
spec matched what should be built, then implement code that matches it exactly — not to write
new documentation. Always check whether the planner pre-wrote the brain content before treating
DO-style tasks as "write from scratch."

T-128 pass-path test used the REAL `SharedKernel.Communication.Grpc` assembly (not a contrived
fixture) via `typeof(SharedKernel.Communication.Grpc.Builders.IGrpcCommunicationBuilder).Assembly`.
This required adding a `<ProjectReference>` (with `PrivateAssets="all"`) to
`11.Communication/SharedKernel.Communication.Grpc/SharedKernel.Communication.Grpc.csproj` in
`SharedKernel.ArchitectureTests.Tests.csproj` — first cross-domain (00→11) real-assembly reference
in this test project; prior tests in this file all used contrived in-memory fixtures.

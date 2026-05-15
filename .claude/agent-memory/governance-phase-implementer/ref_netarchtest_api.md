---
name: ref_netarchtest_api
description: NetArchTest.Rules 1.3.2 actual API surface — ConditionList not IArchRule, Assembly parameter pattern
metadata:
  type: reference
---

## Package

`NetArchTest.Rules` version **1.3.2** (NOT `NetArchTest.eNt` — that is a different fork).

## No IArchRule

`IArchRule` does NOT exist in NetArchTest.Rules 1.3.2. The CLAUDE.md originally referenced it but was corrected.
The fluent API result type is **`ConditionList`** — returned by `.Should().*()` chains.

## Correct fluent pattern

```csharp
ConditionList rule = Types
    .InAssembly(assembly)
    .That().HaveNameStartingWith(string.Empty)
    .Should().NotHaveDependencyOn("SomeNamespace");

TestResult result = rule.GetResult();
result.IsSuccessful // bool
result.FailingTypeNames // IEnumerable<string>?
```

## SharedKernelLayeringRules factory signature

All methods take `Assembly` as a parameter and return `ConditionList`:
```csharp
public static ConditionList DomainNeverReferencesPersistence(Assembly assembly) => ...
```

## ArchitectureRuleBase.AssertRule

```csharp
protected void AssertRule(ConditionList conditionList)
{
    var result = conditionList.GetResult();
    result.IsSuccessful.Should().BeTrue(because: $"Failing types: {string.Join(", ", result.FailingTypeNames ?? [])}");
}
```

## ICustomRule

`ICustomRule` interface DOES exist — used for custom predicates (e.g., DoesNotContainThrowIlPredicate).
`MeetCustomRule(ICustomRule)` is the extension method on `ConditionList` to apply it.

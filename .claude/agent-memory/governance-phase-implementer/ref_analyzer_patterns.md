---
name: ref_analyzer_patterns
description: Roslyn analyzer implementation patterns for 00.Governance — namespace suppression, SK0003 scope, test harness
metadata:
  type: reference
---

## Namespace suppression pattern

Used in SK0001 (and applicable to any rule needing to suppress inside `SharedKernel.Primitives`).
Walk `SyntaxNode.Parent` chain looking for `NamespaceDeclarationSyntax` or `FileScopedNamespaceDeclarationSyntax`.
Check `.Name.ToString().StartsWith("SharedKernel.Primitives")`.
Do NOT use SemanticModel for this — syntax-only is sufficient and avoids semantic model overhead.

```csharp
private static bool IsInsidePrimitivesNamespace(SyntaxNode node)
{
    var current = node.Parent;
    while (current is not null)
    {
        if (current is NamespaceDeclarationSyntax ns && ns.Name.ToString().StartsWith("SharedKernel.Primitives"))
            return true;
        if (current is FileScopedNamespaceDeclarationSyntax fns && fns.Name.ToString().StartsWith("SharedKernel.Primitives"))
            return true;
        current = current.Parent;
    }
    return false;
}
```

## SK0003 scope

SK0003 fires ONLY when the concrete thrown type IS `System.Exception` or `System.ApplicationException`.
Do NOT walk the base-type chain — typed subclasses (DomainException, ArgumentException, etc.) are permitted.

```csharp
private static bool IsForbiddenExceptionType(ITypeSymbol type)
{
    var fullName = type.ToDisplayString();
    return fullName == "System.Exception" || fullName == "System.ApplicationException";
}
```

## SK diagnostic IDs assigned

- SK0001 — DirectDateTimeUsage (Usage, Warning)
- SK0002 — DirectMicrosoftFeatureManagerUsage (Usage, Warning)
- SK0003 — RawExceptionThrow (Design, Warning)
- SK0004 — NullErrorReturn (Design, Warning)
- SK0005 — StringOnlyExceptionConstructor (Design, Warning)
- SK0006 — GuardClauseThrow (Design, Warning) — GuardPurity phase
- SK0007 — RedisChannelServiceMessagingSubstitute (Design, Warning) — CachingEnforcement phase
- SK0008 — AggregateRootDispatchCoupling (Design, Warning)
- SK0009 — DomainEventMissingVersionAttribute (Design, Warning)
- SK0010 — SpecificationOrderingConflict (Design, Warning)
- SK0011 — GuidFormatCodeMisuse (Design, Warning) — requires SemanticModel
- SK0012 — MakeGenericMethodReflection (Design, Warning) — NetArchTest ICustomRule, not Roslyn
- SK0013 — RawHttpClientConstructorInjection (Usage, Warning) — WO-025 P-159
- SK0201 — TenantedDbContextOnModelCreatingGuard (Design, Warning) — multi-tenancy block
- SK0202 — IgnoreQueryFiltersOutsideTenantedRepository (Design, Warning) — multi-tenancy block
- SK0301–SK0304 — Encryption-domain rules (Security/Design, Warning) — 03xx block
- SK0703–SK0708 — Messaging-domain rules (Usage/Design, Warning) — 07xx block
- Next available in general block: SK0014
- Next available in 02xx (multi-tenancy): SK0203
- Next available in 03xx (encryption): SK0305
- Next available in 07xx (messaging): SK0709

## SK0013 DelegatingHandler exemption pattern

SK0013 fires on `HttpClient` constructor parameters. Two exemptions apply:

1. **Namespace exemption**: namespace starts with `SharedKernel.Communication.Rest`
2. **Base-class exemption**: enclosing `ClassDeclarationSyntax` has `DelegatingHandler` in `BaseList`

```csharp
private static bool IsInsideRestNamespace(SyntaxNode node)
{
    var current = node.Parent;
    while (current is not null)
    {
        if (current is NamespaceDeclarationSyntax ns &&
            ns.Name.ToString().StartsWith("SharedKernel.Communication.Rest"))
            return true;
        if (current is FileScopedNamespaceDeclarationSyntax fns &&
            fns.Name.ToString().StartsWith("SharedKernel.Communication.Rest"))
            return true;
        current = current.Parent;
    }
    return false;
}

private static bool IsInsideDelegatingHandler(SyntaxNode node)
{
    var classDecl = node.FirstAncestorOrSelf<ClassDeclarationSyntax>();
    if (classDecl?.BaseList is null) return false;
    return classDecl.BaseList.Types.Any(t =>
        GetSimpleTypeName(t.Type) == "DelegatingHandler");
}
```

The DelegatingHandler exemption applies regardless of namespace — delegating handlers legitimately accept `HttpClient` as the inner handler in the chain.

## IHttpClientFactory in analyzer test fixtures

`IHttpClientFactory` lives in `Microsoft.Extensions.Http` which is NOT in the default analyzer test harness compilation context. For pass-path tests that use `IHttpClientFactory`, stub it inline in the `TestCode`:

```csharp
// Stub IHttpClientFactory for compilation — the rule is syntax-only
public interface IHttpClientFactory
{
    HttpClient CreateClient(string name);
}
```

This avoids the CS0246 compile error in the test harness without adding an extra NuGet reference.

## Analyzer test harness pattern

Use `CSharpAnalyzerTest<TAnalyzer, DefaultVerifier>` from `Microsoft.CodeAnalysis.CSharp.Testing`.
NOT `XUnitVerifier` — use `DefaultVerifier`.

Fire-path test — use inline diagnostic markup:
```csharp
var test = new CSharpAnalyzerTest<MyAnalyzer, DefaultVerifier>
{
    TestCode = """
        namespace Foo { public class Bar { void M() => {|SK0001:DateTime.UtcNow|}; } }
        """
};
await test.RunAsync();
```

Pass-path test — no markup, leave TestCode clean:
```csharp
var test = new CSharpAnalyzerTest<MyAnalyzer, DefaultVerifier>
{
    TestCode = """
        namespace Foo { public class Bar { void M() { } } }
        """
};
await test.RunAsync();
```

## AnalyzerBase.CreateDescriptor HelpLinkBase

```
https://github.com/gresta-vertex-labs/platform-shared-kernel/blob/main/00.Governance/README.md
```
Anchor pattern: `#{lowercase-id}-{lowercase-rule-name}` e.g. `#sk0001-directdatetimeusage`

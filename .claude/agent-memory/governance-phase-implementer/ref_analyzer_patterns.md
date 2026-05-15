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
- Next available: SK0007

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

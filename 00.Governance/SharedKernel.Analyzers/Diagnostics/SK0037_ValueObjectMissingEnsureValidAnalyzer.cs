using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SharedKernel.Analyzers.Diagnostics;

/// <summary>
/// SK0037 — Fires on a concrete class deriving from <c>SharedKernel.Domain.ValueObjects.ValueObject</c> when a
/// constructor can complete without calling <c>EnsureValid()</c>, so the rules in its <c>Validate()</c> never run.
/// </summary>
/// <remarks>
/// <para>
/// <c>ValueObject</c>'s base constructor does not validate; a value object assigns its members and calls
/// <c>EnsureValid()</c> as its constructor's last statement. Omitting the call compiles and silently produces
/// unvalidated values, the one trap left in that model.
/// </para>
/// <para>
/// <strong>Detection.</strong> Base types are resolved with the semantic model, matching
/// <c>SharedKernel.Domain.ValueObjects.ValueObject</c> by fully qualified name. Each class in the chain between
/// the reported class and <c>ValueObject</c> that is declared in source is inspected. The reported class is
/// compliant when every instance constructor of some class in that chain either calls <c>EnsureValid()</c> or
/// delegates with <c>: this(...)</c>. A class declaring no constructor, or only a primary constructor, has no body
/// that can call it.
/// </para>
/// <para>
/// <strong>Not flagged:</strong> abstract classes (their concrete subclasses are checked); anything deriving from
/// <c>SingleValueObject&lt;TValue&gt;</c>, which calls <c>EnsureValid()</c> itself; and a class whose effective
/// <c>Validate()</c> override declares no rules — an expression body of <c>[]</c>, <c>null</c>, <c>default</c>,
/// <c>Array.Empty&lt;…&gt;()</c> or <c>Enumerable.Empty&lt;…&gt;()</c>, or a block containing only
/// <c>yield break;</c> or <c>return</c> of one of those. Such a value object has nothing to ensure. Generated code
/// is skipped.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ValueObjectMissingEnsureValidAnalyzer : AnalyzerBase
{
    private const string DiagnosticId = "SK0037";
    private const string ValueObjectMetadataName = "SharedKernel.Domain.ValueObjects.ValueObject";
    private const string SingleValueObjectMetadataName = "SharedKernel.Domain.ValueObjects.SingleValueObject`1";
    private const string EnsureValidName = "EnsureValid";
    private const string ValidateName = "Validate";

    /// <summary>The diagnostic descriptor for SK0037.</summary>
    public static readonly DiagnosticDescriptor Rule = CreateDescriptor(
        id: DiagnosticId,
        title: "Value object constructor never calls EnsureValid",
        messageFormat: "'{0}' derives from ValueObject but a constructor completes without calling EnsureValid(), "
            + "so the rules in Validate() never run. Call EnsureValid() as the last statement of every constructor.",
        category: Design,
        defaultSeverity: DiagnosticSeverity.Warning,
        readmeAnchor: "sk0037-valueobjectmissingensurevalid"
    );

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(AnalyzeNamedType, SymbolKind.NamedType);
    }

    private static void AnalyzeNamedType(SymbolAnalysisContext context)
    {
        var type = (INamedTypeSymbol)context.Symbol;
        if (type.TypeKind != TypeKind.Class || type.IsAbstract || type.IsStatic)
            return;

        var chain = new List<INamedTypeSymbol>();
        var reachesValueObject = false;
        for (var current = type; current is not null; current = current.BaseType)
        {
            var name = MetadataName(current.OriginalDefinition);
            if (name == SingleValueObjectMetadataName)
                return;
            if (name == ValueObjectMetadataName)
            {
                reachesValueObject = true;
                break;
            }

            chain.Add(current);
        }

        if (!reachesValueObject || HasNoValidationRules(chain, context.CancellationToken))
            return;

        if (chain.Any(declared => AllConstructorsEnsureValid(declared, context.CancellationToken)))
            return;

        var location = type.Locations.FirstOrDefault(l => l.IsInSource);
        if (location is not null)
            context.ReportDiagnostic(Diagnostic.Create(Rule, location, type.Name));
    }

    private static bool AllConstructorsEnsureValid(INamedTypeSymbol type, System.Threading.CancellationToken cancellationToken)
    {
        var constructors = type.DeclaringSyntaxReferences
            .Select(reference => reference.GetSyntax(cancellationToken))
            .OfType<TypeDeclarationSyntax>()
            .SelectMany(declaration => declaration.Members.OfType<ConstructorDeclarationSyntax>())
            .Where(constructor => !constructor.Modifiers.Any(SyntaxKind.StaticKeyword))
            .ToList();

        if (constructors.Count == 0)
            return false;

        return constructors.All(constructor =>
            constructor.Initializer?.IsKind(SyntaxKind.ThisConstructorInitializer) == true
            || CallsEnsureValid(constructor));
    }

    private static bool CallsEnsureValid(ConstructorDeclarationSyntax constructor)
    {
        SyntaxNode? body = (SyntaxNode?)constructor.Body ?? constructor.ExpressionBody;
        if (body is null)
            return false;

        return body.DescendantNodesAndSelf()
            .OfType<InvocationExpressionSyntax>()
            .Any(invocation => invocation.Expression switch
            {
                IdentifierNameSyntax identifier => identifier.Identifier.Text == EnsureValidName,
                MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax or BaseExpressionSyntax } access =>
                    access.Name.Identifier.Text == EnsureValidName,
                _ => false,
            });
    }

    private static bool HasNoValidationRules(List<INamedTypeSymbol> chain, System.Threading.CancellationToken cancellationToken)
    {
        // The most-derived override wins; the chain is ordered from the reported class toward ValueObject.
        foreach (var declared in chain)
        {
            var validate = declared.GetMembers(ValidateName)
                .OfType<IMethodSymbol>()
                .FirstOrDefault(method => method.IsOverride && method.Parameters.Length == 0);
            if (validate is null)
                continue;

            var syntax = validate.DeclaringSyntaxReferences
                .Select(reference => reference.GetSyntax(cancellationToken))
                .OfType<MethodDeclarationSyntax>()
                .FirstOrDefault();

            return syntax is not null && IsTrivial(syntax);
        }

        return false;
    }

    private static bool IsTrivial(MethodDeclarationSyntax method)
    {
        if (method.ExpressionBody is not null)
            return IsEmptyExpression(method.ExpressionBody.Expression);

        if (method.Body is null)
            return false;

        var statements = method.Body.Statements;
        return statements.Count == 1 && statements[0] switch
        {
            YieldStatementSyntax yield => yield.IsKind(SyntaxKind.YieldBreakStatement),
            ReturnStatementSyntax { Expression: { } expression } => IsEmptyExpression(expression),
            _ => false,
        };
    }

    private static bool IsEmptyExpression(ExpressionSyntax expression)
    {
        switch (expression)
        {
            case CollectionExpressionSyntax collection:
                return collection.Elements.Count == 0;
            case LiteralExpressionSyntax literal:
                return literal.IsKind(SyntaxKind.NullLiteralExpression) || literal.IsKind(SyntaxKind.DefaultLiteralExpression);
            case InvocationExpressionSyntax { ArgumentList.Arguments.Count: 0 } invocation:
                var name = invocation.Expression switch
                {
                    MemberAccessExpressionSyntax access => access.Name.Identifier.Text,
                    _ => string.Empty,
                };
                return name == "Empty";
            default:
                return false;
        }
    }

    private static string MetadataName(INamedTypeSymbol symbol)
    {
        var containingNamespace = symbol.ContainingNamespace;
        return containingNamespace is null || containingNamespace.IsGlobalNamespace
            ? symbol.MetadataName
            : $"{containingNamespace.ToDisplayString()}.{symbol.MetadataName}";
    }
}

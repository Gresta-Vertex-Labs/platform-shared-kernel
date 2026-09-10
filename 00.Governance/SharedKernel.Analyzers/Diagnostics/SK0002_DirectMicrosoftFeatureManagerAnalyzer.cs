using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SharedKernel.Analyzers.Diagnostics;

/// <summary>
/// SK0002 — Fires when a <see cref="ParameterSyntax"/>, <see cref="FieldDeclarationSyntax"/>, or
/// <see cref="PropertyDeclarationSyntax"/> is typed as
/// <c>Microsoft.FeatureManagement.IFeatureManager</c>, resolved via
/// <see cref="SemanticModel.GetTypeInfo(Microsoft.CodeAnalysis.SyntaxNode, System.Threading.CancellationToken)"/>.
/// </summary>
/// <remarks>
/// <para>
/// The platform wraps the third-party feature-flag package behind its own
/// <c>SharedKernel.FeatureManagement.IFeatureManager</c> abstraction so consuming services never
/// take a hard, unmediated dependency on that package's exact API surface. Referencing the
/// Microsoft interface directly defeats the abstraction the moment it happens — nothing then
/// stands between the consumer and a future breaking change in the underlying package.
/// </para>
/// <para>
/// <strong>Registration.</strong> Three independent <see cref="SyntaxKind"/> registrations — one
/// each for <see cref="SyntaxKind.Parameter"/>, <see cref="SyntaxKind.FieldDeclaration"/>, and
/// <see cref="SyntaxKind.PropertyDeclaration"/> — every one resolving its own declared
/// <see cref="TypeSyntax"/> against the <see cref="SemanticModel"/> and comparing
/// <c>symbol.ToDisplayString()</c> against the forbidden fully-qualified name. No single shared
/// "any type reference" registration is used, since a parameter, a field, and a property each
/// expose their declared type through a differently-shaped syntax node.
/// </para>
/// <para>
/// <strong>Unresolved-symbol fallback.</strong> When type resolution returns a
/// <see langword="null"/> symbol — the type genuinely fails to bind, e.g. because a reference is
/// missing from the compilation — the check falls back to a syntax-only simple-name comparison
/// against <c>"IFeatureManager"</c>. This is a defensive fallback for an already-degraded
/// compilation, not the analyzer's primary path; the primary path is always the fully-qualified-name
/// comparison, so a same-simple-named but unrelated interface that resolves cleanly never
/// false-positives.
/// </para>
/// <para>
/// <strong>Pass case:</strong> <c>SharedKernel.FeatureManagement.IFeatureManager</c> — a distinct
/// fully-qualified name sharing only the simple name <c>IFeatureManager</c> — never matches on the
/// primary (resolved) path, since the comparison is against the full display string, not the
/// simple name.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DirectMicrosoftFeatureManagerAnalyzer : AnalyzerBase
{
    private const string DiagnosticId = "SK0002";
    private const string ForbiddenFullName = "Microsoft.FeatureManagement.IFeatureManager";
    private const string ForbiddenShortName = "IFeatureManager";

    /// <summary>The diagnostic descriptor for SK0002.</summary>
    public static readonly DiagnosticDescriptor Rule = CreateDescriptor(
        id: DiagnosticId,
        title: "Direct Microsoft.FeatureManagement.IFeatureManager usage",
        messageFormat: "Do not reference 'Microsoft.FeatureManagement.IFeatureManager' directly — use 'SharedKernel.FeatureManagement.IFeatureManager' instead",
        category: Usage,
        defaultSeverity: DiagnosticSeverity.Warning,
        readmeAnchor: "sk0002-directmicrosoftfeaturemanagerusage"
    );

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        // Check constructor parameters
        context.RegisterSyntaxNodeAction(AnalyzeParameter, SyntaxKind.Parameter);

        // Check field declarations
        context.RegisterSyntaxNodeAction(AnalyzeFieldDeclaration, SyntaxKind.FieldDeclaration);

        // Check property declarations
        context.RegisterSyntaxNodeAction(AnalyzePropertyDeclaration, SyntaxKind.PropertyDeclaration);
    }

    private static void AnalyzeParameter(SyntaxNodeAnalysisContext context)
    {
        var parameter = (ParameterSyntax)context.Node;
        if (parameter.Type is null)
            return;

        if (!IsForbiddenFeatureManagerType(parameter.Type, context.SemanticModel))
            return;

        context.ReportDiagnostic(Diagnostic.Create(Rule, parameter.Type.GetLocation()));
    }

    private static void AnalyzeFieldDeclaration(SyntaxNodeAnalysisContext context)
    {
        var field = (FieldDeclarationSyntax)context.Node;
        if (!IsForbiddenFeatureManagerType(field.Declaration.Type, context.SemanticModel))
            return;

        context.ReportDiagnostic(
            Diagnostic.Create(Rule, field.Declaration.Type.GetLocation())
        );
    }

    private static void AnalyzePropertyDeclaration(SyntaxNodeAnalysisContext context)
    {
        var property = (PropertyDeclarationSyntax)context.Node;
        if (!IsForbiddenFeatureManagerType(property.Type, context.SemanticModel))
            return;

        context.ReportDiagnostic(Diagnostic.Create(Rule, property.Type.GetLocation()));
    }

    private static bool IsForbiddenFeatureManagerType(
        TypeSyntax typeSyntax,
        SemanticModel semanticModel
    )
    {
        var typeInfo = semanticModel.GetTypeInfo(typeSyntax);
        var symbol = typeInfo.Type;

        if (symbol is null)
        {
            // Fallback: syntax-only check for unresolved symbols
            return ExtractTypeName(typeSyntax) == ForbiddenShortName;
        }

        return symbol.ToDisplayString() == ForbiddenFullName;
    }

    private static string? ExtractTypeName(TypeSyntax typeSyntax) =>
        typeSyntax switch
        {
            IdentifierNameSyntax id => id.Identifier.ValueText,
            QualifiedNameSyntax q => q.Right.Identifier.ValueText,
            _ => null,
        };
}

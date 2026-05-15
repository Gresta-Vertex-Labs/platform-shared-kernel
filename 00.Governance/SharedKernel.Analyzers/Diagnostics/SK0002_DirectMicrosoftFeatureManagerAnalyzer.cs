using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SharedKernel.Analyzers.Diagnostics;

/// <summary>
/// SK0002 — Fires when <c>Microsoft.FeatureManagement.IFeatureManager</c> is referenced
/// as a constructor parameter type, field declaration type, or property declaration type.
/// Inject <c>SharedKernel.FeatureManagement.IFeatureManager</c> instead.
/// </summary>
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

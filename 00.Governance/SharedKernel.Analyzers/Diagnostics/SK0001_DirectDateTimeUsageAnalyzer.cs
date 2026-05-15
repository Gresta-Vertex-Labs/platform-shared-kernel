using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SharedKernel.Analyzers.Diagnostics;

/// <summary>
/// SK0001 — Fires when code directly accesses <c>DateTime.UtcNow</c>, <c>DateTime.Now</c>,
/// or <c>DateTimeOffset.UtcNow</c> outside the <c>SharedKernel.Primitives</c> namespace.
/// Inject <c>IClock</c> via DI instead.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DirectDateTimeUsageAnalyzer : AnalyzerBase
{
    private const string DiagnosticId = "SK0001";

    /// <summary>The diagnostic descriptor for SK0001.</summary>
    public static readonly DiagnosticDescriptor Rule = CreateDescriptor(
        id: DiagnosticId,
        title: "Direct DateTime/DateTimeOffset usage",
        messageFormat: "Direct access to '{0}' is not allowed — inject IClock via DI instead",
        category: Usage,
        defaultSeverity: DiagnosticSeverity.Warning,
        readmeAnchor: "sk0001-directdatetimeusage"
    );

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSyntaxNodeAction(
            AnalyzeMemberAccess,
            SyntaxKind.SimpleMemberAccessExpression
        );
    }

    private static void AnalyzeMemberAccess(SyntaxNodeAnalysisContext context)
    {
        var memberAccess = (MemberAccessExpressionSyntax)context.Node;

        // Must be: SomeType.MemberName
        if (memberAccess.Expression is not IdentifierNameSyntax typeIdentifier)
            return;

        var typeName = typeIdentifier.Identifier.ValueText;
        var memberName = memberAccess.Name.Identifier.ValueText;

        bool isViolation =
            (typeName == "DateTime" && (memberName == "UtcNow" || memberName == "Now"))
            || (typeName == "DateTimeOffset" && memberName == "UtcNow");

        if (!isViolation)
            return;

        // Suppress inside SharedKernel.Primitives namespace
        if (IsInsidePrimitivesNamespace(memberAccess))
            return;

        var fullAccess = $"{typeName}.{memberName}";
        context.ReportDiagnostic(
            Diagnostic.Create(Rule, memberAccess.GetLocation(), fullAccess)
        );
    }

    private static bool IsInsidePrimitivesNamespace(SyntaxNode node)
    {
        var current = node.Parent;
        while (current is not null)
        {
            if (
                current is NamespaceDeclarationSyntax namespaceDecl
                && namespaceDecl.Name.ToString().StartsWith("SharedKernel.Primitives")
            )
                return true;

            if (
                current is FileScopedNamespaceDeclarationSyntax fileScopedNs
                && fileScopedNs.Name.ToString().StartsWith("SharedKernel.Primitives")
            )
                return true;

            current = current.Parent;
        }

        return false;
    }
}

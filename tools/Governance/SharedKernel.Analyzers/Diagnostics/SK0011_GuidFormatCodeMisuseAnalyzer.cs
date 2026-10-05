using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SharedKernel.Analyzers.Diagnostics;

/// <summary>
/// SK0011 — Fires when <c>Guid.ToString(string)</c> is called with a format argument
/// whose value is <c>"N"</c>, <c>"B"</c>, <c>"P"</c>, or <c>"X"</c> (case-insensitive).
/// </summary>
/// <remarks>
/// <para>
/// The canonical GUID string format for audit column values (<c>CreatedBy</c>,
/// <c>ModifiedBy</c>) is the lowercase hyphenated form produced by <c>ToString()</c> or
/// <c>ToString("D")</c> — e.g., <c>"d3e4f5a6-1b2c-3d4e-5f6a-7b8c9d0e1f2a"</c>.
/// Using format codes <c>"N"</c> (no hyphens), <c>"B"</c> (braces), <c>"P"</c>
/// (parentheses), or <c>"X"</c> (hex) produces representations that diverge from this
/// canonical format, causing inconsistent values across services sharing the same audit schema.
/// </para>
/// <para>
/// <strong>Receiver-type guard:</strong> this analyzer requires a minimal
/// <see cref="SemanticModel.GetTypeInfo"/> call on the receiver expression to confirm it is
/// <c>System.Guid</c>. This prevents false positives from non-Guid <c>ToString("N")</c>
/// calls (e.g., numeric format specifiers on <c>int</c> or <c>double</c>).
/// SK0011 is the first SK analyzer to require a semantic model check.
/// </para>
/// <para>
/// <strong>Suppression:</strong> no suppression namespace is defined. Suppress per-call-site
/// via <c>#pragma warning disable SK0011</c> when a compact format is genuinely required
/// (e.g., a URL segment). Document the suppression with a comment explaining why the
/// non-canonical format is acceptable at that call site.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class GuidFormatCodeMisuseAnalyzer : AnalyzerBase
{
    private const string DiagnosticId = "SK0011";

    /// <summary>The diagnostic descriptor for SK0011.</summary>
    public static readonly DiagnosticDescriptor Rule = CreateDescriptor(
        id: DiagnosticId,
        title: "Guid.ToString called with non-canonical format code",
        messageFormat: "Guid.ToString(\"{0}\") produces a non-canonical format — use ToString() or ToString(\"D\") for the hyphenated lowercase format required by audit column values",
        category: Design,
        defaultSeverity: DiagnosticSeverity.Warning,
        readmeAnchor: "sk0011-guidformatcodemisuse"
    );

    /// <summary>Format codes that produce non-canonical GUID string representations.</summary>
    private static readonly ImmutableHashSet<string> ForbiddenFormats =
        ImmutableHashSet.Create(
            System.StringComparer.OrdinalIgnoreCase,
            "N", "B", "P", "X");

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSyntaxNodeAction(
            AnalyzeInvocation,
            SyntaxKind.InvocationExpression
        );
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        // Must be a member access expression: <receiver>.ToString(<arg>)
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            return;

        // Method name must be "ToString"
        if (!string.Equals(
            memberAccess.Name.Identifier.Text,
            "ToString",
            System.StringComparison.Ordinal))
        {
            return;
        }

        // Must have exactly one argument
        var arguments = invocation.ArgumentList.Arguments;
        if (arguments.Count != 1)
            return;

        // That argument must be a string literal
        var argument = arguments[0].Expression;
        if (argument is not LiteralExpressionSyntax literal)
            return;

        if (!literal.IsKind(SyntaxKind.StringLiteralExpression))
            return;

        // Extract the format code value (strip surrounding quotes)
        var formatValue = literal.Token.ValueText;

        // Must be a forbidden format code (case-insensitive)
        if (!ForbiddenFormats.Contains(formatValue))
            return;

        // Semantic model check: receiver must be System.Guid
        var receiverTypeInfo = context.SemanticModel.GetTypeInfo(
            memberAccess.Expression,
            context.CancellationToken);

        var receiverType = receiverTypeInfo.Type;
        if (receiverType is null)
            return;

        // Verify that the receiver type is System.Guid
        if (!IsSystemGuid(receiverType))
            return;

        // Report SK0011 on the format argument literal token
        context.ReportDiagnostic(
            Diagnostic.Create(Rule, literal.GetLocation(), formatValue));
    }

    private static bool IsSystemGuid(ITypeSymbol type)
    {
        return string.Equals(type.Name, "Guid", System.StringComparison.Ordinal)
            && type.ContainingNamespace is not null
            && string.Equals(
                type.ContainingNamespace.ToString(),
                "System",
                System.StringComparison.Ordinal);
    }
}

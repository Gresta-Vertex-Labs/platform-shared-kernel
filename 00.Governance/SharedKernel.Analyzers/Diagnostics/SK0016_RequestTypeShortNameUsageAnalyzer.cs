using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SharedKernel.Analyzers.Diagnostics;

/// <summary>
/// SK0016 — Fires on a standalone <c>typeof(X).Name</c> member access found inside a file whose
/// namespace declaration starts with <c>SharedKernel.Application</c> (covers both
/// <c>SharedKernel.Application</c> and <c>SharedKernel.Application.Behaviors</c>), unless the
/// member access is the right-hand operand of a <c>??</c> coalesce expression whose left-hand
/// operand is <c>typeof(X).FullName</c> for the syntactically-identical <c>X</c>.
/// </summary>
/// <remarks>
/// <para>
/// Two request types with the same short name in different namespaces or assemblies collide under
/// <c>typeof(X).Name</c> alone. Any metric tag, log scope key, or cache key that must remain unique
/// across assemblies must use <c>typeof(TRequest).FullName ?? typeof(TRequest).Name</c> — the
/// collision-safe pattern mandated by P-231.
/// </para>
/// <para>
/// <strong>Trigger shape (inverse of the usual exemption).</strong> Unlike SK0001/SK0007/SK0013,
/// which trigger everywhere except inside a documented exemption namespace, SK0016 is
/// namespace-scoped as a trigger-<em>IN</em> condition — the collision risk this rule targets is
/// intrinsic to MediatR request-type tag/key construction, which lives exclusively in
/// <c>SharedKernel.Application</c>*.
/// </para>
/// <para>
/// This is a <strong>syntax-only</strong> check — no <see cref="SemanticModel"/> is required. The
/// <c>FullName</c>-coalesce companion is detected via a textual match on the left operand's type
/// argument against the flagged <c>.Name</c> access's type argument.
/// </para>
/// <para>
/// <b>Fires on:</b> <c>typeof(TRequest).Name</c> used standalone.
/// </para>
/// <para>
/// <b>Does not fire on:</b> <c>typeof(TRequest).FullName ?? typeof(TRequest).Name</c>, or any
/// <c>typeof(X).Name</c> usage outside <c>SharedKernel.Application</c>*.
/// </para>
/// <para>
/// <b>Suppression:</b> use <c>#pragma warning disable SK0016</c> at the call site when the short
/// name is genuinely sufficient (e.g. a user-facing display string where collision risk is
/// irrelevant); document the rationale inline.
/// </para>
/// <para>Introduced WO-038 P-235, closing the P-231 fix's mechanical-enforcement gap.</para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class RequestTypeShortNameUsageAnalyzer : AnalyzerBase
{
    private const string DiagnosticId = "SK0016";
    private const string NameMemberName = "Name";
    private const string FullNameMemberName = "FullName";
    private const string ExemptedNamespacePrefix = "SharedKernel.Application";

    /// <summary>The diagnostic descriptor for SK0016.</summary>
    public static readonly DiagnosticDescriptor Rule = CreateDescriptor(
        id: DiagnosticId,
        title: "typeof(X).Name used without a FullName companion",
        messageFormat: "typeof({0}).Name is not collision-safe across assemblies. Use " +
                       "typeof({0}).FullName ?? typeof({0}).Name for any metric tag, log scope key, " +
                       "or cache key that must remain unique.",
        category: Design,
        defaultSeverity: DiagnosticSeverity.Warning,
        readmeAnchor: "sk0016-requesttypeshortnameusage"
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

        if (memberAccess.Name.Identifier.Text != NameMemberName)
            return;

        if (memberAccess.Expression is not TypeOfExpressionSyntax typeOfExpr)
            return;

        if (!IsInsideExemptedNamespace(memberAccess))
            return;

        if (HasFullNameCoalesceCompanion(memberAccess, typeOfExpr))
            return;

        context.ReportDiagnostic(
            Diagnostic.Create(Rule, memberAccess.GetLocation(), typeOfExpr.Type.ToString())
        );
    }

    /// <summary>
    /// Trigger-IN namespace check (inverse of the usual exemption walk) — this rule fires only
    /// inside <see cref="ExemptedNamespacePrefix"/>, not everywhere except it. Same
    /// <see cref="SyntaxNode.Parent"/> walk pattern as SK0001/SK0007/SK0013.
    /// </summary>
    private static bool IsInsideExemptedNamespace(SyntaxNode node)
    {
        var current = node.Parent;
        while (current is not null)
        {
            string? nsName = current switch
            {
                NamespaceDeclarationSyntax ns => ns.Name.ToString(),
                FileScopedNamespaceDeclarationSyntax fsns => fsns.Name.ToString(),
                _ => null,
            };

            if (nsName is not null &&
                nsName.StartsWith(ExemptedNamespacePrefix, StringComparison.Ordinal))
            {
                return true;
            }

            current = current.Parent;
        }

        return false;
    }

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="nameAccess"/> is the right-hand
    /// operand of a <c>??</c> coalesce expression whose left-hand operand is
    /// <c>typeof(X).FullName</c> for the syntactically-identical <paramref name="typeOfExpr"/>.
    /// </summary>
    private static bool HasFullNameCoalesceCompanion(
        MemberAccessExpressionSyntax nameAccess,
        TypeOfExpressionSyntax typeOfExpr)
    {
        if (nameAccess.Parent is not BinaryExpressionSyntax binary ||
            !binary.IsKind(SyntaxKind.CoalesceExpression))
        {
            return false;
        }

        if (binary.Right != nameAccess)
            return false;

        if (binary.Left is not MemberAccessExpressionSyntax fullNameAccess ||
            fullNameAccess.Name.Identifier.Text != FullNameMemberName)
        {
            return false;
        }

        if (fullNameAccess.Expression is not TypeOfExpressionSyntax leftTypeOfExpr)
            return false;

        return leftTypeOfExpr.Type.ToString() == typeOfExpr.Type.ToString();
    }
}

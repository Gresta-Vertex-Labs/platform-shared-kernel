using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SharedKernel.Analyzers.Diagnostics;

/// <summary>
/// SK0202 — Fires when <c>IgnoreQueryFilters()</c> is invoked (zero arguments) outside the
/// <c>SharedKernel.Persistence.EfCore*</c> namespace and outside a class named
/// <c>TenantedRepository</c>.
/// </summary>
/// <remarks>
/// <para>
/// <c>IgnoreQueryFilters()</c> bypasses all EF Core global query filters registered on a
/// <see cref="Microsoft.EntityFrameworkCore.DbContext"/>, including the tenant filter applied
/// by <c>TenantedDbContext</c>. Calling it outside the persistence layer or the designated
/// repository base leaks cross-tenant data to callers without any compiler or runtime warning.
/// </para>
/// <para>
/// <b>Exemptions:</b>
/// <list type="bullet">
///   <item>Any containing namespace that starts with <c>SharedKernel.Persistence.EfCore</c> —
///         the persistence implementation layer is allowed to use <c>IgnoreQueryFilters()</c>
///         deliberately (e.g., soft-delete cleanup jobs).</item>
///   <item>Any class named <c>TenantedRepository</c> (exact match) — the designated
///         cross-tenant query base class is the single sanctioned call site outside the
///         platform namespace.</item>
/// </list>
/// To add a new exemption, document it in <c>00.Governance/CLAUDE.md</c> under the SK0202
/// rule entry before applying <c>#pragma warning disable SK0202</c> at the call site.
/// </para>
/// <para>
/// Suppression: use <c>#pragma warning disable SK0202</c> at the invocation site with an
/// inline comment explaining the cross-tenant access rationale.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class IgnoreQueryFiltersOutsideTenantedRepositoryAnalyzer : AnalyzerBase
{
    private const string DiagnosticId = "SK0202";

    private const string IgnoreQueryFiltersName = "IgnoreQueryFilters";
    private const string ExemptClassSimpleName = "TenantedRepository";
    private const string ExemptNamespacePrefix = "SharedKernel.Persistence.EfCore";

    /// <summary>The diagnostic descriptor for SK0202.</summary>
    public static readonly DiagnosticDescriptor Rule = CreateDescriptor(
        id: DiagnosticId,
        title: "IgnoreQueryFilters() called outside permitted persistence scope",
        messageFormat: "'IgnoreQueryFilters()' must only be called inside the 'SharedKernel.Persistence.EfCore' namespace or from a class named 'TenantedRepository' — unrestricted use silently bypasses the global tenant query filter",
        category: Design,
        defaultSeverity: DiagnosticSeverity.Warning,
        readmeAnchor: "sk0202-ignorequeryfiltersoutsidetenantedrepository"
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
            AnalyzeInvocation,
            SyntaxKind.InvocationExpression
        );
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        // Filter to IgnoreQueryFilters with zero arguments
        if (!IsIgnoreQueryFiltersCall(invocation))
            return;

        // Exemption 1: namespace starts with SharedKernel.Persistence.EfCore
        if (IsInsideExemptNamespace(invocation))
            return;

        // Exemption 2: containing class is named TenantedRepository (exact match)
        if (IsInsideTenantedRepositoryClass(invocation))
            return;

        context.ReportDiagnostic(
            Diagnostic.Create(Rule, invocation.GetLocation())
        );
    }

    private static bool IsIgnoreQueryFiltersCall(InvocationExpressionSyntax invocation)
    {
        // Must have zero arguments
        if (invocation.ArgumentList.Arguments.Count != 0)
            return false;

        // Extract the simple method name
        var methodName = invocation.Expression switch
        {
            IdentifierNameSyntax id => id.Identifier.Text,
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.Text,
            _ => null,
        };

        return methodName == IgnoreQueryFiltersName;
    }

    /// <summary>
    /// Walks <paramref name="node"/>'s ancestors looking for a namespace declaration whose
    /// qualified name starts with <c>SharedKernel.Persistence.EfCore</c>.
    /// </summary>
    private static bool IsInsideExemptNamespace(SyntaxNode node)
    {
        var current = node.Parent;
        while (current is not null)
        {
            if (
                current is NamespaceDeclarationSyntax namespaceDecl
                && namespaceDecl.Name.ToString().StartsWith(ExemptNamespacePrefix)
            )
                return true;

            if (
                current is FileScopedNamespaceDeclarationSyntax fileScopedNs
                && fileScopedNs.Name.ToString().StartsWith(ExemptNamespacePrefix)
            )
                return true;

            current = current.Parent;
        }

        return false;
    }

    /// <summary>
    /// Returns <see langword="true"/> when the invocation is inside a class whose
    /// <c>Identifier.Text</c> is exactly <c>"TenantedRepository"</c>.
    /// </summary>
    private static bool IsInsideTenantedRepositoryClass(SyntaxNode node)
    {
        var containingClass = node.FirstAncestorOrSelf<ClassDeclarationSyntax>();
        return containingClass?.Identifier.Text == ExemptClassSimpleName;
    }
}

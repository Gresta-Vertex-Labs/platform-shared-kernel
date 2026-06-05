using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SharedKernel.Analyzers.Diagnostics;

/// <summary>
/// SK0201 — Fires when a class that inherits from <c>TenantedDbContext</c> (by simple name)
/// overrides <c>OnModelCreating</c> without calling <c>base.OnModelCreating(...)</c> or
/// <c>ApplyTenantFilters(...)</c> inside the method body.
/// </summary>
/// <remarks>
/// <para>
/// <c>TenantedDbContext.OnModelCreating</c> registers the global tenant query filter. Any subclass
/// that overrides the method and omits <c>base.OnModelCreating</c> (or a manual
/// <c>ApplyTenantFilters</c> call) silently removes the filter, causing all queries to return
/// rows across tenant boundaries — a silent multi-tenancy data leak.
/// </para>
/// <para>
/// <b>Limitation:</b> This analyzer performs a syntax-only check within a single file. If the
/// inheritance chain spans multiple files or assemblies (e.g., the class extends an intermediate
/// class that itself extends <c>TenantedDbContext</c>), only the immediate <c>BaseList</c> is
/// inspected. Cross-file or cross-assembly ancestry is not resolved.
/// </para>
/// <para>
/// Suppression: use <c>#pragma warning disable SK0201</c> at the method site when the tenant
/// filter is intentionally re-applied via a different mechanism not detectable at syntax level.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class TenantedDbContextOnModelCreatingAnalyzer : AnalyzerBase
{
    private const string DiagnosticId = "SK0201";

    private const string TenantedDbContextSimpleName = "TenantedDbContext";
    private const string OnModelCreatingName = "OnModelCreating";
    private const string ApplyTenantFiltersName = "ApplyTenantFilters";

    /// <summary>The diagnostic descriptor for SK0201.</summary>
    public static readonly DiagnosticDescriptor Rule = CreateDescriptor(
        id: DiagnosticId,
        title: "TenantedDbContext.OnModelCreating override missing tenant-filter call",
        messageFormat: "'{0}.OnModelCreating' overrides TenantedDbContext but does not call 'base.OnModelCreating' or 'ApplyTenantFilters' — the global tenant query filter will be silently removed",
        category: Design,
        defaultSeverity: DiagnosticSeverity.Warning,
        readmeAnchor: "sk0201-tenanteddbcontextonmodelcreatingguard"
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
            AnalyzeMethodDeclaration,
            SyntaxKind.MethodDeclaration
        );
    }

    private static void AnalyzeMethodDeclaration(SyntaxNodeAnalysisContext context)
    {
        var method = (MethodDeclarationSyntax)context.Node;

        // Must be named OnModelCreating with the override modifier
        if (method.Identifier.Text != OnModelCreatingName)
            return;

        if (!method.Modifiers.Any(SyntaxKind.OverrideKeyword))
            return;

        // Find the containing class declaration
        var containingClass = method.FirstAncestorOrSelf<ClassDeclarationSyntax>();
        if (containingClass is null)
            return;

        // Check whether the class (or any ancestor class in the same file) inherits TenantedDbContext
        if (!InheritsTenantedDbContext(containingClass))
            return;

        // Inspect the method body for base.OnModelCreating(...) or ApplyTenantFilters(...)
        if (method.Body is null && method.ExpressionBody is null)
        {
            // Abstract / extern — no body to check; skip
            return;
        }

        if (BodyContainsTenantCall(method))
            return;

        // Neither call found — report on the method identifier
        var className = containingClass.Identifier.Text;
        context.ReportDiagnostic(
            Diagnostic.Create(Rule, method.Identifier.GetLocation(), className)
        );
    }

    /// <summary>
    /// Checks whether <paramref name="classDecl"/> or any of its enclosing class declarations in
    /// the same compilation unit declares <c>TenantedDbContext</c> as a direct base type (simple
    /// name match).
    /// </summary>
    private static bool InheritsTenantedDbContext(ClassDeclarationSyntax classDecl)
    {
        // Walk from the immediate class upward through enclosing class declarations
        SyntaxNode? current = classDecl;
        while (current is ClassDeclarationSyntax currentClass)
        {
            if (HasTenantedDbContextInBaseList(currentClass))
                return true;

            current = current.Parent;
        }

        return false;
    }

    private static bool HasTenantedDbContextInBaseList(ClassDeclarationSyntax classDecl)
    {
        if (classDecl.BaseList is null)
            return false;

        foreach (var baseType in classDecl.BaseList.Types)
        {
            var simpleName = GetSimpleTypeName(baseType.Type);
            if (simpleName == TenantedDbContextSimpleName)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Returns <see langword="true"/> when the method body contains either:
    /// <list type="bullet">
    ///   <item><c>base.OnModelCreating(...)</c> — a member-access on <c>base</c></item>
    ///   <item><c>ApplyTenantFilters(...)</c> — a simple or member-access invocation</item>
    /// </list>
    /// </summary>
    private static bool BodyContainsTenantCall(MethodDeclarationSyntax method)
    {
        SyntaxNode bodyRoot = (SyntaxNode?)method.Body ?? method.ExpressionBody!;

        foreach (var invocation in bodyRoot.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            switch (invocation.Expression)
            {
                // base.OnModelCreating(...)
                case MemberAccessExpressionSyntax memberAccess
                    when memberAccess.Expression is BaseExpressionSyntax
                        && memberAccess.Name.Identifier.Text == OnModelCreatingName:
                    return true;

                // ApplyTenantFilters(...) — simple name call: ApplyTenantFilters(...)
                case IdentifierNameSyntax identifierName
                    when identifierName.Identifier.Text == ApplyTenantFiltersName:
                    return true;

                // this.ApplyTenantFilters(...) or any_obj.ApplyTenantFilters(...)
                case MemberAccessExpressionSyntax memberAccess2
                    when memberAccess2.Name.Identifier.Text == ApplyTenantFiltersName:
                    return true;
            }
        }

        return false;
    }

    private static string GetSimpleTypeName(TypeSyntax typeSyntax) =>
        typeSyntax switch
        {
            IdentifierNameSyntax id => id.Identifier.Text,
            GenericNameSyntax gen => gen.Identifier.Text,
            NullableTypeSyntax nullable => GetSimpleTypeName(nullable.ElementType),
            QualifiedNameSyntax qualified => qualified.Right.Identifier.Text,
            _ => typeSyntax.ToString(),
        };
}

using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SharedKernel.Analyzers.Diagnostics;

/// <summary>
/// SK0201 — Fires when a class that inherits from <c>TenantedDbContext</c> (by simple name)
/// overrides <c>OnModelCreating</c> without calling <c>base.OnModelCreating(...)</c> inside the method body.
/// </summary>
/// <remarks>
/// <para>
/// The tenant query filter itself is installed by a model-finalizing convention and survives a missing base call,
/// but <c>SharedKernelDbContext.OnModelCreating</c> applies the entity type configurations of the context's
/// assembly, the registered model configurators, the <c>Money</c> mapping and client-side key generation. An
/// override that omits <c>base.OnModelCreating</c> silently drops all of them, so the model no longer matches what
/// the platform (and its tenant, audit and encryption configuration) expects.
/// </para>
/// <para>
/// <b>Limitation:</b> This analyzer performs a syntax-only check within a single file. If the
/// inheritance chain spans multiple files or assemblies (e.g., the class extends an intermediate
/// class that itself extends <c>TenantedDbContext</c>), only the immediate <c>BaseList</c> is
/// inspected. Cross-file or cross-assembly ancestry is not resolved.
/// </para>
/// <para>
/// Suppression: use <c>#pragma warning disable SK0201</c> at the method site when the base call is made through a
/// helper the syntax check cannot see.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class TenantedDbContextOnModelCreatingAnalyzer : AnalyzerBase
{
    private const string DiagnosticId = "SK0201";

    private const string TenantedDbContextSimpleName = "TenantedDbContext";
    private const string OnModelCreatingName = "OnModelCreating";

    /// <summary>The diagnostic descriptor for SK0201.</summary>
    public static readonly DiagnosticDescriptor Rule = CreateDescriptor(
        id: DiagnosticId,
        title: "TenantedDbContext.OnModelCreating override missing base call",
        messageFormat: "'{0}.OnModelCreating' overrides TenantedDbContext but does not call 'base.OnModelCreating' — the platform model configuration (entity configurations, Money mapping, key generation) is silently skipped",
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

        // Inspect the method body for base.OnModelCreating(...)
        if (method.Body is null && method.ExpressionBody is null)
        {
            // Abstract / extern — no body to check; skip
            return;
        }

        if (BodyCallsBase(method))
            return;

        // No base call found — report on the method identifier
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

    /// <summary>Returns <see langword="true"/> when the method body contains <c>base.OnModelCreating(...)</c>.</summary>
    private static bool BodyCallsBase(MethodDeclarationSyntax method)
    {
        SyntaxNode bodyRoot = (SyntaxNode?)method.Body ?? method.ExpressionBody!;

        return bodyRoot
            .DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Any(invocation =>
                invocation.Expression is MemberAccessExpressionSyntax memberAccess
                && memberAccess.Expression is BaseExpressionSyntax
                && memberAccess.Name.Identifier.Text == OnModelCreatingName);
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

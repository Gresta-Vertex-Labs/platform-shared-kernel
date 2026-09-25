using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SharedKernel.Analyzers.Diagnostics;

/// <summary>
/// SK0031 — Fires when a constructor declares a parameter whose type is a
/// <see cref="SimpleNameSyntax"/> or <see cref="IdentifierNameSyntax"/> whose identifier text is
/// exactly one of <c>IHttpContextAccessor</c>, <c>ClaimsPrincipal</c>, or <c>HttpContext</c>,
/// unless the enclosing type sits inside a namespace whose qualified name starts with
/// <c>SharedKernel.Security.Oidc</c> or <c>SharedKernel.Security.ApiKey</c>.
/// </summary>
/// <remarks>
/// <para>
/// Mirrors SK0013's (<see cref="RawHttpClientConstructorInjectionAnalyzer"/>) exact syntax-only
/// shape and <see cref="SyntaxNode.Parent"/> namespace-ancestor exemption walk — a domain-specific
/// instance of the "raw framework/infrastructure primitive injected outside its owning
/// abstraction package" prohibition family (SK0013, SK0026, SK0029). No <see cref="SemanticModel"/>
/// is required.
/// </para>
/// <para>
/// <strong>Two exemption namespace prefixes, not one:</strong> <c>SharedKernel.Security.Oidc</c>
/// (the real, shipped implementation package that legitimately constructs
/// <c>OidcUserContext</c>/<c>OidcTenantProvider</c> from a <c>ClaimsPrincipal</c>/
/// <c>IHttpContextAccessor</c>) and <c>SharedKernel.Security.ApiKey</c> — named verbatim in this
/// phase's own input as a forward-looking, currently-vacuous exemption prefix. If a future
/// API-key provider package ever ships under a different name, this exemption must be revised in
/// the same PR, mirroring the maintenance-obligation discipline already established for
/// <c>SharedKernelLayeringRules</c>'s forbidden-term lists.
/// </para>
/// <para>
/// <strong>Fix:</strong> inject <c>SharedKernel.Security.Abstractions.IUserContext</c> (for
/// identity) or <c>SharedKernel.Execution.Context.IRequestContext</c> (for tenant identity) instead of a raw
/// <c>HttpContext</c>-family type. Application-layer and domain-adjacent code must never reach
/// past the platform's identity/tenant abstraction into ASP.NET Core hosting internals.
/// </para>
/// <para>
/// <strong>Suppression:</strong> per-constructor via <c>#pragma warning disable SK0031</c> when a
/// raw <c>HttpContext</c>-family type is genuinely required (e.g., a middleware component);
/// document the rationale inline.
/// </para>
/// <para>
/// Introduced in WO-057 P-373, the platform's first <c>12.Security</c>-domain diagnostic.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class RawSecurityContextConstructorInjectionAnalyzer : AnalyzerBase
{
    private const string DiagnosticId = "SK0031";

    /// <summary>The exact simple type names that trigger this rule.</summary>
    private static readonly ImmutableHashSet<string> ForbiddenTypeNames = ImmutableHashSet.Create(
        "IHttpContextAccessor",
        "ClaimsPrincipal",
        "HttpContext"
    );

    /// <summary>
    /// The namespace prefixes that suppress this diagnostic — types inside either package
    /// legitimately construct <c>IUserContext</c>/<c>IRequestContext</c> implementations from raw
    /// <c>HttpContext</c>-family types.
    /// </summary>
    private static readonly ImmutableArray<string> ExemptedNamespacePrefixes = ImmutableArray.Create(
        "SharedKernel.Security.Oidc",
        "SharedKernel.Security.ApiKey"
    );

    /// <summary>The diagnostic descriptor for SK0031.</summary>
    public static readonly DiagnosticDescriptor Rule = CreateDescriptor(
        id: DiagnosticId,
        title: "Raw security-context constructor injection",
        messageFormat: "Constructor parameter '{0}' is typed as '{1}' directly. " +
                       "Inject SharedKernel.Security.Abstractions.IUserContext (for identity) or " +
                       "SharedKernel.Execution.Context.IRequestContext (for tenant identity) instead of a raw HttpContext-family " +
                       "type. Application-layer and domain-adjacent code must never reach past the " +
                       "platform's identity/tenant abstraction into ASP.NET Core hosting internals.",
        category: Usage,
        defaultSeverity: DiagnosticSeverity.Warning,
        readmeAnchor: "sk0031-rawsecuritycontextconstructorinjection"
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
            AnalyzeConstructor,
            SyntaxKind.ConstructorDeclaration
        );
    }

    private static void AnalyzeConstructor(SyntaxNodeAnalysisContext context)
    {
        var ctorDecl = (ConstructorDeclarationSyntax)context.Node;

        // Exemption — namespace walk: suppress inside SharedKernel.Security.Oidc/.ApiKey.
        if (IsInsideExemptedNamespace(ctorDecl))
            return;

        foreach (var param in ctorDecl.ParameterList.Parameters)
        {
            if (param.Type is null)
                continue;

            var simpleName = GetSimpleTypeName(param.Type);
            if (simpleName is not null && ForbiddenTypeNames.Contains(simpleName))
            {
                context.ReportDiagnostic(
                    Diagnostic.Create(Rule, param.Type.GetLocation(), param.Identifier.Text, simpleName)
                );
            }
        }
    }

    private static string? GetSimpleTypeName(TypeSyntax typeSyntax)
    {
        return typeSyntax switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.Text,
            QualifiedNameSyntax qualified => qualified.Right.Identifier.Text,
            _ => null,
        };
    }

    /// <summary>
    /// Walks ancestor syntax nodes looking for a namespace declaration whose qualified name
    /// starts with one of <see cref="ExemptedNamespacePrefixes"/>. Same pattern as
    /// SK0001/SK0007/SK0013/SK0026.
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

            if (nsName is not null)
            {
                foreach (var prefix in ExemptedNamespacePrefixes)
                {
                    if (nsName.StartsWith(prefix, System.StringComparison.Ordinal))
                        return true;
                }
            }

            current = current.Parent;
        }

        return false;
    }
}

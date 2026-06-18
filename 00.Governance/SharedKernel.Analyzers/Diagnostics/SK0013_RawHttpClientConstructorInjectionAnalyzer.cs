using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SharedKernel.Analyzers.Diagnostics;

/// <summary>
/// SK0013 — Fires when a constructor declares a parameter whose type is exactly
/// <c>HttpClient</c>, and neither of the following exemptions applies:
/// <list type="bullet">
///   <item>
///     <description>
///       Namespace exemption: any ancestor <see cref="NamespaceDeclarationSyntax"/> or
///       <see cref="FileScopedNamespaceDeclarationSyntax"/> whose qualified name starts with
///       <c>SharedKernel.Communication.Rest</c> — the REST typed-client package legitimately
///       manages <c>HttpClient</c> instances internally.
///     </description>
///   </item>
///   <item>
///     <description>
///       Base-class exemption: the enclosing <see cref="ClassDeclarationSyntax"/> has a base list
///       containing a type whose simple name is exactly <c>DelegatingHandler</c> — delegating
///       handlers receive the inner <c>HttpClient</c> as part of the handler chain and must not be
///       widened without a governance review.
///     </description>
///   </item>
/// </list>
/// </summary>
/// <remarks>
/// <para>
/// Direct <c>HttpClient</c> injection bypasses connection pooling, DNS refresh cycles, and
/// handler lifetime management — all of which are production reliability concerns for .NET
/// microservices. The correct pattern is to use <c>IHttpClientFactory</c>-managed typed clients
/// registered via <c>AddRestClient&lt;TClient&gt;()</c>.
/// </para>
/// <para>
/// <strong>Suppression:</strong> per-constructor suppression via
/// <c>#pragma warning disable SK0013</c> is permitted only when raw <c>HttpClient</c> injection
/// is genuinely required (e.g., a unit-test helper). Document the rationale inline.
/// </para>
/// <para>
/// <strong>Note:</strong> this is a syntax-only check; no <see cref="SemanticModel"/> is required.
/// Introduced in WO-025 P-159.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class RawHttpClientConstructorInjectionAnalyzer : AnalyzerBase
{
    private const string DiagnosticId = "SK0013";

    /// <summary>The exact simple type name that triggers this rule.</summary>
    private const string HttpClientTypeName = "HttpClient";

    /// <summary>
    /// The simple base-class name whose presence on the declaring class exempts constructors
    /// from this rule.
    /// </summary>
    private const string DelegatingHandlerTypeName = "DelegatingHandler";

    /// <summary>
    /// The namespace prefix that suppresses this diagnostic — types inside
    /// <c>SharedKernel.Communication.Rest</c> legitimately manage <c>HttpClient</c> internally.
    /// </summary>
    private const string ExemptedNamespacePrefix = "SharedKernel.Communication.Rest";

    /// <summary>The diagnostic descriptor for SK0013.</summary>
    public static readonly DiagnosticDescriptor Rule = CreateDescriptor(
        id: DiagnosticId,
        title: "Raw HttpClient injection in constructor",
        messageFormat: "Constructor parameter '{0}' is typed as HttpClient directly. " +
                       "Inject the named typed-client interface (TClient) via IHttpClientFactory-managed " +
                       "AddRestClient<TClient>() instead. " +
                       "Direct HttpClient injection bypasses connection pooling, DNS refresh cycles, " +
                       "and handler lifetime management.",
        category: Usage,
        defaultSeverity: DiagnosticSeverity.Warning,
        readmeAnchor: "sk0013-rawhttpclientconstructorinjection"
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

        // Exemption (a): namespace walk — suppress inside SharedKernel.Communication.Rest
        if (IsInsideExemptedNamespace(ctorDecl))
            return;

        // Exemption (b): enclosing class derives from DelegatingHandler
        if (IsInsideDelegatingHandlerSubclass(ctorDecl))
            return;

        // Check each constructor parameter for the exact type name "HttpClient"
        foreach (var param in ctorDecl.ParameterList.Parameters)
        {
            if (param.Type is not null && IsHttpClientType(param.Type))
            {
                var paramName = param.Identifier.Text;
                context.ReportDiagnostic(
                    Diagnostic.Create(Rule, param.Type.GetLocation(), paramName)
                );
            }
        }
    }

    private static bool IsHttpClientType(TypeSyntax typeSyntax)
    {
        return GetSimpleTypeName(typeSyntax) == HttpClientTypeName;
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
    /// Exemption (a): walks ancestor syntax nodes looking for a namespace declaration whose
    /// qualified name starts with <see cref="ExemptedNamespacePrefix"/>.
    /// Same pattern as SK0001/SK0007/SK0202.
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
                nsName.StartsWith(ExemptedNamespacePrefix, System.StringComparison.Ordinal))
            {
                return true;
            }

            current = current.Parent;
        }

        return false;
    }

    /// <summary>
    /// Exemption (b): the constructor's enclosing <see cref="ClassDeclarationSyntax"/> declares
    /// a base list that contains a type whose simple name is exactly
    /// <see cref="DelegatingHandlerTypeName"/>.
    /// </summary>
    private static bool IsInsideDelegatingHandlerSubclass(SyntaxNode node)
    {
        // Walk up to the enclosing class declaration
        var current = node.Parent;
        while (current is not null)
        {
            if (current is ClassDeclarationSyntax classDecl)
            {
                return HasDelegatingHandlerBase(classDecl);
            }

            current = current.Parent;
        }

        return false;
    }

    private static bool HasDelegatingHandlerBase(ClassDeclarationSyntax classDecl)
    {
        if (classDecl.BaseList is null)
            return false;

        foreach (var baseType in classDecl.BaseList.Types)
        {
            var simpleName = GetSimpleTypeName(baseType.Type);
            if (simpleName == DelegatingHandlerTypeName)
                return true;
        }

        return false;
    }
}

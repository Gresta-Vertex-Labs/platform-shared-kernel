using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SharedKernel.Analyzers.Diagnostics;

/// <summary>
/// SK0008 — Fires when a <see cref="ConstructorDeclarationSyntax"/> parameter's type text
/// CONTAINS the substring <c>IAggregateRoot</c> (covering both the open interface
/// <c>IAggregateRoot</c> and the closed generic <c>IAggregateRoot&lt;TId&gt;</c>), inside a class
/// whose name or any enclosing namespace identifier contains one of <c>Interceptor</c>,
/// <c>Publisher</c>, <c>Outbox</c>, or <c>Dispatcher</c>.
/// </summary>
/// <remarks>
/// <para>
/// Dispatch infrastructure only needs to raise domain events — it does not need the full
/// aggregate identity surface. Injecting <c>IAggregateRoot&lt;TId&gt;</c> instead of
/// <c>IHasDomainEvents</c> creates an unnecessary coupling to aggregate identity in dispatch code.
/// </para>
/// <para>
/// <strong>Registration.</strong> Registers only on <see cref="SyntaxKind.ConstructorDeclaration"/>
/// — unlike SK0007's sibling analyzer, field and property declarations are never inspected here,
/// only constructor parameters.
/// </para>
/// <para>
/// <strong>Substring type match, not exact.</strong> <c>IsAggregateRootType</c> is a
/// <c>Contains("IAggregateRoot")</c> check on the parameter's type identifier text, not an equality
/// check — so it matches the bare interface, its closed-generic form, AND any differently-named
/// type whose simple name happens to contain that substring (e.g. a hypothetical
/// <c>IAggregateRootRepository</c>). This mirrors SK0007's context-matching style and is an
/// accepted, documented trade-off rather than an oversight — no <see cref="SemanticModel"/> is
/// consulted, so no compiled reference to the real <c>IAggregateRoot&lt;TId&gt;</c> type is needed
/// for the rule to fire.
/// </para>
/// <para>
/// No suppression namespace is defined for SK0008. The rule fires in all namespaces where
/// dispatch-context names appear.
/// </para>
/// <para>
/// <strong>Pass cases:</strong> a constructor parameter typed <c>IHasDomainEvents</c> inside the
/// same dispatch-context class never matches the substring check and passes; conversely, an
/// <c>IAggregateRoot&lt;TId&gt;</c> parameter in a class with no dispatch-context term in its name
/// or namespace (e.g. a domain-layer factory) never reaches the type check at all, since the
/// dispatch-context gate is evaluated first.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AggregateRootDispatchCouplingAnalyzer : AnalyzerBase
{
    private const string DiagnosticId = "SK0008";

    private static readonly string[] DispatchContextTerms =
    [
        "Interceptor",
        "Publisher",
        "Outbox",
        "Dispatcher",
    ];

    /// <summary>The diagnostic descriptor for SK0008.</summary>
    public static readonly DiagnosticDescriptor Rule = CreateDescriptor(
        id: DiagnosticId,
        title: "Dispatch code should not couple to IAggregateRoot",
        messageFormat: "Constructor parameter '{0}' in dispatch-context class '{1}' is typed as IAggregateRoot — inject IHasDomainEvents instead for narrower coupling",
        category: Design,
        defaultSeverity: DiagnosticSeverity.Warning,
        readmeAnchor: "sk0008-aggregaterootdispatchcoupling"
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
        var ctor = (ConstructorDeclarationSyntax)context.Node;

        // Get the containing class declaration
        var classDecl = ctor.Parent as ClassDeclarationSyntax;
        if (classDecl is null)
            return;

        // Check whether the class or any ancestor namespace is a dispatch context
        if (!IsDispatchContext(classDecl))
            return;

        // Inspect each constructor parameter for IAggregateRoot type
        foreach (var param in ctor.ParameterList.Parameters)
        {
            if (param.Type is null)
                continue;

            if (IsAggregateRootType(param.Type))
            {
                var location = param.Type.GetLocation();
                var paramName = param.Identifier.Text;
                var className = classDecl.Identifier.Text;

                context.ReportDiagnostic(
                    Diagnostic.Create(Rule, location, paramName, className)
                );
            }
        }
    }

    private static bool IsAggregateRootType(TypeSyntax typeSyntax)
    {
        var name = GetTypeIdentifierText(typeSyntax);
        return name.Contains("IAggregateRoot", System.StringComparison.Ordinal);
    }

    private static string GetTypeIdentifierText(TypeSyntax typeSyntax)
    {
        return typeSyntax switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.Text,
            GenericNameSyntax generic => generic.Identifier.Text,
            NullableTypeSyntax nullable => GetTypeIdentifierText(nullable.ElementType),
            QualifiedNameSyntax qualified => qualified.Right.Identifier.Text,
            _ => typeSyntax.ToString(),
        };
    }

    private static bool IsDispatchContext(ClassDeclarationSyntax classDecl)
    {
        // Check class name
        var className = classDecl.Identifier.Text;
        foreach (var term in DispatchContextTerms)
        {
            if (className.Contains(term, System.StringComparison.Ordinal))
                return true;
        }

        // Walk ancestor namespaces
        var current = classDecl.Parent;
        while (current is not null)
        {
            string? nsPart = current switch
            {
                NamespaceDeclarationSyntax ns => ns.Name.ToString(),
                FileScopedNamespaceDeclarationSyntax fsns => fsns.Name.ToString(),
                _ => null,
            };

            if (nsPart is not null)
            {
                foreach (var term in DispatchContextTerms)
                {
                    if (nsPart.Contains(term, System.StringComparison.Ordinal))
                        return true;
                }
            }

            current = current.Parent;
        }

        return false;
    }
}

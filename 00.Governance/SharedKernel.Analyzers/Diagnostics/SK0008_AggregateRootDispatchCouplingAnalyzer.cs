using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SharedKernel.Analyzers.Diagnostics;

/// <summary>
/// SK0008 — Fires when a constructor parameter is typed as <c>IAggregateRoot&lt;TId&gt;</c>
/// (simple name contains <c>"IAggregateRoot"</c>) inside a class whose name or any enclosing
/// namespace identifier contains any of: <c>"Interceptor"</c>, <c>"Publisher"</c>,
/// <c>"Outbox"</c>, <c>"Dispatcher"</c>.
/// </summary>
/// <remarks>
/// <para>
/// Dispatch infrastructure only needs to raise domain events — it does not need the full
/// aggregate identity surface. Injecting <c>IAggregateRoot&lt;TId&gt;</c> instead of
/// <c>IHasDomainEvents</c> creates an unnecessary coupling to aggregate identity in dispatch code.
/// </para>
/// <para>
/// No suppression namespace is defined for SK0008. The rule fires in all namespaces where
/// dispatch-context names appear.
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

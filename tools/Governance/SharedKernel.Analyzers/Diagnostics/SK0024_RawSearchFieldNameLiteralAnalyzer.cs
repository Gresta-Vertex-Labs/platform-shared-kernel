using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SharedKernel.Analyzers.Diagnostics;

/// <summary>
/// SK0024 — bans a raw string-literal token at the field-name parameter position of one of eleven
/// recognized <c>SharedKernel.Search.Abstractions</c> call-site shapes: six on
/// <c>IQueryBuilder&lt;TDocument&gt;</c>/<c>SearchQueryBuilder&lt;TDocument&gt;</c>
/// (<c>OrderBy</c>, <c>OrderByDescending</c>, <c>SearchingIn</c>, <c>Faceting</c>,
/// <c>WithNumericFacetStats</c>, <c>Returning</c>) and five on <c>SearchFilter</c>'s static
/// factories (<c>Eq</c>, <c>Ne</c>, <c>In</c>, <c>Between</c>, <c>Exists</c>, field always at
/// argument position 0).
/// </summary>
/// <remarks>
/// <para>
/// <strong>Refactor-safety intent, not cross-cutting wire-contract intent.</strong> SK0024 shares
/// SK0022's literal-vs-reference syntax-shape discriminator (a <see cref="LiteralExpressionSyntax"/>
/// of kind <see cref="SyntaxKind.StringLiteralExpression"/> at the checked position, declaring-class
/// agnostic — a <c>nameof(...)</c> expression compiles to an <see cref="InvocationExpressionSyntax"/>,
/// not a literal, so it passes automatically, and a domain-local field-constants class reference
/// passes identically) but its motivating hazard is DIFFERENT: a typo'd field name is a visible,
/// pre-I/O rejection on Meilisearch (<c>SearchErrors.FieldNotFilterable</c>/
/// <c>FieldNotSortable</c>/<c>FieldNotFacetable</c>) and a SILENT ZERO-RESULT on ElasticSearch
/// whenever the typo happens to also be a syntactically legal but nonexistent field reference at the
/// ES query-DSL level. Do not merge this rule into SK0022 or generalize SK0022 to cover it — the two
/// entries intentionally do not cross-reference each other's rationale.
/// </para>
/// <para>
/// Each of the eleven shapes requires <see cref="SemanticModel.GetSymbolInfo(SyntaxNode, System.Threading.CancellationToken)"/>
/// to resolve the invoked method to its exact declaring type — a syntax-only simple-name check on
/// <c>OrderBy</c>/<c>Where</c>/<c>In</c>/<c>Exists</c> would collide catastrophically with LINQ's own
/// <see cref="System.Linq.Enumerable"/>/<see cref="System.Linq.Queryable"/> extension methods of the
/// same names.
/// </para>
/// <para>
/// For the four <c>params string[]</c> shapes (<c>SearchingIn</c>, <c>Faceting</c>,
/// <c>WithNumericFacetStats</c>, <c>Returning</c>), EVERY argument expression supplied at that
/// parameter position is checked individually — covers both the multi-argument call form
/// (<c>SearchingIn("a", "b")</c>) and any array/collection-expression form
/// (<c>SearchingIn(new[] { "a", "b" })</c> / <c>SearchingIn(["a", "b"])</c>).
/// </para>
/// <para>No suppression namespace — SK0024 fires globally, like SK0022. Introduced in WO-044 P-278.</para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class RawSearchFieldNameLiteralAnalyzer : AnalyzerBase
{
    private const string DiagnosticId = "SK0024";

    private const string QueryBuilderInterfaceFullName = "SharedKernel.Search.Abstractions.Querying.IQueryBuilder";
    private const string SearchQueryBuilderFullName = "SharedKernel.Search.Abstractions.Querying.SearchQueryBuilder";
    private const string SearchFilterFullName = "SharedKernel.Search.Abstractions.Models.SearchFilter";

    private static readonly HashSet<string> QueryBuilderSingleArgMethods = new(StringComparer.Ordinal)
    {
        "OrderBy",
        "OrderByDescending",
    };

    private static readonly HashSet<string> QueryBuilderParamsMethods = new(StringComparer.Ordinal)
    {
        "SearchingIn",
        "Faceting",
        "WithNumericFacetStats",
        "Returning",
    };

    private static readonly HashSet<string> SearchFilterSingleArgMethods = new(StringComparer.Ordinal)
    {
        "Eq",
        "Ne",
        "In",
        "Between",
        "Exists",
    };

    /// <summary>The diagnostic descriptor for SK0024.</summary>
    public static readonly DiagnosticDescriptor Rule = CreateDescriptor(
        id: DiagnosticId,
        title: "Raw string literal in a search field-name position",
        messageFormat: "Raw string literal supplied as a search field name. Reference the field via "
            + "nameof(TDocument.PropertyName) or a domain-local field-constants class member instead "
            + "— never a raw string literal. A typo'd literal is a visible rejection on Meilisearch "
            + "and a silent zero-result on ElasticSearch.",
        category: Usage,
        defaultSeverity: DiagnosticSeverity.Warning,
        readmeAnchor: "sk0024-rawsearchfieldnameliteral"
    );

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        var symbolInfo = context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken);
        if (symbolInfo.Symbol is not IMethodSymbol methodSymbol)
            return;

        var containingType = methodSymbol.ContainingType?.OriginalDefinition;
        if (containingType is null)
            return;

        var containingTypeFullName = GetFullTypeName(containingType);
        var methodName = methodSymbol.Name;

        if (
            containingTypeFullName is QueryBuilderInterfaceFullName or SearchQueryBuilderFullName
        )
        {
            if (QueryBuilderSingleArgMethods.Contains(methodName))
            {
                ReportIfLiteralArgument(context, invocation, argumentIndex: 0);
                return;
            }

            if (QueryBuilderParamsMethods.Contains(methodName))
            {
                ReportLiteralsInParamsArguments(context, invocation);
                return;
            }

            return;
        }

        if (containingTypeFullName == SearchFilterFullName && SearchFilterSingleArgMethods.Contains(methodName))
        {
            ReportIfLiteralArgument(context, invocation, argumentIndex: 0);
        }
    }

    private static void ReportIfLiteralArgument(
        SyntaxNodeAnalysisContext context,
        InvocationExpressionSyntax invocation,
        int argumentIndex
    )
    {
        var arguments = invocation.ArgumentList.Arguments;
        if (argumentIndex < 0 || argumentIndex >= arguments.Count)
            return;

        if (TryGetStringLiteral(arguments[argumentIndex].Expression, out var literal))
        {
            Report(context, literal);
        }
    }

    private static void ReportLiteralsInParamsArguments(
        SyntaxNodeAnalysisContext context,
        InvocationExpressionSyntax invocation
    )
    {
        foreach (var argument in invocation.ArgumentList.Arguments)
        {
            ReportLiteralsInExpression(context, argument.Expression);
        }
    }

    private static void ReportLiteralsInExpression(SyntaxNodeAnalysisContext context, ExpressionSyntax expression)
    {
        if (TryGetStringLiteral(expression, out var literal))
        {
            Report(context, literal);
            return;
        }

        switch (expression)
        {
            case ArrayCreationExpressionSyntax { Initializer: { } arrayInitializer }:
                foreach (var element in arrayInitializer.Expressions)
                {
                    ReportLiteralsInExpression(context, element);
                }
                break;

            case ImplicitArrayCreationExpressionSyntax { Initializer: { } implicitInitializer }:
                foreach (var element in implicitInitializer.Expressions)
                {
                    ReportLiteralsInExpression(context, element);
                }
                break;

            case CollectionExpressionSyntax collectionExpression:
                foreach (var element in collectionExpression.Elements)
                {
                    if (element is ExpressionElementSyntax expressionElement)
                    {
                        ReportLiteralsInExpression(context, expressionElement.Expression);
                    }
                }
                break;
        }
    }

    private static bool TryGetStringLiteral(ExpressionSyntax expression, out LiteralExpressionSyntax literal)
    {
        if (expression is LiteralExpressionSyntax candidate && candidate.IsKind(SyntaxKind.StringLiteralExpression))
        {
            literal = candidate;
            return true;
        }

        literal = null!;
        return false;
    }

    private static void Report(SyntaxNodeAnalysisContext context, LiteralExpressionSyntax literal)
    {
        context.ReportDiagnostic(Diagnostic.Create(Rule, literal.GetLocation()));
    }

    private static string GetFullTypeName(ITypeSymbol type) =>
        type.ContainingNamespace is { IsGlobalNamespace: false }
            ? $"{type.ContainingNamespace.ToDisplayString()}.{type.Name}"
            : type.Name;
}

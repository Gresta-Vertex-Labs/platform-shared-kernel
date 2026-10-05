using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SharedKernel.Analyzers.Diagnostics;

/// <summary>
/// SK0027 — bans a raw string-literal token at the collection-name/field-name/embedding-model-id
/// parameter position of one of eight recognized <c>SharedKernel.AI.Abstractions</c> call-site
/// shapes: five on <c>VectorFilter</c>'s static factories (<c>Eq</c>, <c>Ne</c>, <c>In</c>,
/// <c>Between</c>, <c>Exists</c>, field always at argument position 0) and three on
/// <c>IVectorCollectionProvisioner</c> (<c>CollectionExistsAsync</c>, <c>DeleteCollectionAsync</c>,
/// <c>ProbeAsync</c>, <c>collectionName</c> always position 0) — plus both string parameters of
/// <c>VectorCollectionDefinition.Create</c> (<c>name</c> at position 0, <c>embeddingModelId</c> at
/// position 1, checked independently) and both single-string-parameter members of
/// <c>VectorCollectionDefinitionBuilder</c> (<c>EmbeddingModel</c>, <c>Field</c>, position 0 each).
/// </summary>
/// <remarks>
/// <para>
/// SK0027 is structurally parallel to SK0024 (<c>RawSearchFieldNameLiteral</c>) — same
/// literal-vs-reference syntax-shape discriminator (a <see cref="LiteralExpressionSyntax"/> of kind
/// <see cref="SyntaxKind.StringLiteralExpression"/> at the checked position, declaring-class
/// agnostic: a <c>nameof(...)</c> expression or a domain-local identifier-constants class member
/// passes automatically) — but its motivating hazard is DIFFERENT FROM and sharper than SK0024's,
/// and the two entries must not cross-reference each other's rationale, the same discipline already
/// established between SK0022 and SK0024. A typo'd collection name is
/// <c>IntelligenceErrors.CollectionNotFound</c> (visible, checked before I/O) on BOTH providers —
/// symmetric-visible, unlike SK0024's Meilisearch-visible/ElasticSearch-silent asymmetry. The
/// sharper hazard here is <c>embeddingModelId</c>: a <c>VectorCollectionDefinition.Create</c>/
/// <c>VectorCollectionDefinitionBuilder.EmbeddingModel</c> call site and the record/query call sites
/// that must independently supply the SAME <c>embeddingModelId</c> string have no shared
/// compile-time link — a copy-pasted, typo'd literal at both places passes the
/// <c>IVectorRecord.ModelId == VectorCollectionDefinition.EmbeddingModelId</c> guard cleanly,
/// silently embedding/querying the corpus under a phantom model-identity string with zero
/// engine-detectable error on either provider — Domain Invariant #1's exact "no engine can detect a
/// model-identity mismatch" hazard, reproduced by a literal-copy-paste-typo instead of a genuine
/// two-model mixup.
/// </para>
/// <para>
/// Each shape requires <see cref="SemanticModel.GetSymbolInfo(SyntaxNode, System.Threading.CancellationToken)"/>
/// to resolve the invoked member to its exact declaring type — the eight (and related) call-site
/// method names (<c>Eq</c>, <c>Field</c>, etc.) are common enough to collide with unrelated types
/// without an exact-declaring-type check, the same discipline SK0024 applies.
/// </para>
/// <para>
/// <c>VectorCollectionCutoverRequest.StagingCollectionName</c>/<c>.LiveCollectionName</c> are
/// object-initializer PROPERTY assignments, not method-call arguments, and are explicitly OUT OF
/// SCOPE for this rule's call-site-argument-position detection technique — the same documented
/// method-call-only limitation SK0024 carries.
/// </para>
/// <para>No suppression namespace — SK0027 fires globally, like SK0024/SK0022. Introduced in
/// WO-045 P-286. This domain's eleventh semantic-model analyzer (after SK0011, SK0015,
/// SK0017–SK0019, SK0020, SK0022, SK0024, SK0025, SK0026).</para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class RawIntelligenceIdentifierLiteralAnalyzer : AnalyzerBase
{
    private const string DiagnosticId = "SK0027";

    private const string VectorFilterFullName = "SharedKernel.AI.Abstractions.Models.VectorFilter";
    private const string ProvisionerFullName =
        "SharedKernel.AI.Abstractions.Abstractions.IVectorCollectionProvisioner";
    private const string DefinitionFullName =
        "SharedKernel.AI.Abstractions.Models.VectorCollectionDefinition";
    private const string BuilderFullName =
        "SharedKernel.AI.Abstractions.Models.VectorCollectionDefinitionBuilder";

    private const string CreateMethodName = "Create";

    private static readonly HashSet<string> VectorFilterMethods = new(StringComparer.Ordinal)
    {
        "Eq",
        "Ne",
        "In",
        "Between",
        "Exists",
    };

    private static readonly HashSet<string> ProvisionerMethods = new(StringComparer.Ordinal)
    {
        "CollectionExistsAsync",
        "DeleteCollectionAsync",
        "ProbeAsync",
    };

    private static readonly HashSet<string> BuilderMethods = new(StringComparer.Ordinal)
    {
        "EmbeddingModel",
        "Field",
    };

    /// <summary>The diagnostic descriptor for SK0027.</summary>
    public static readonly DiagnosticDescriptor Rule = CreateDescriptor(
        id: DiagnosticId,
        title: "Raw string literal in a vector-collection identifier position",
        messageFormat: "Raw string literal supplied as a collection name, field name, or embedding "
            + "model id. Reference the identifier via nameof(...) or a domain-local "
            + "identifier-constants class member instead — never a raw string literal. A "
            + "copy-pasted, typo'd embeddingModelId literal silently embeds/queries the corpus "
            + "under a phantom model identity with zero engine-detectable error.",
        category: Usage,
        defaultSeverity: DiagnosticSeverity.Warning,
        readmeAnchor: "sk0027-rawintelligenceidentifierliteral"
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

        if (containingTypeFullName == VectorFilterFullName && VectorFilterMethods.Contains(methodName))
        {
            ReportIfLiteralArgument(context, invocation, argumentIndex: 0);
            return;
        }

        if (containingTypeFullName == ProvisionerFullName && ProvisionerMethods.Contains(methodName))
        {
            ReportIfLiteralArgument(context, invocation, argumentIndex: 0);
            return;
        }

        if (containingTypeFullName == DefinitionFullName && methodName == CreateMethodName)
        {
            ReportIfLiteralArgument(context, invocation, argumentIndex: 0);
            ReportIfLiteralArgument(context, invocation, argumentIndex: 1);
            return;
        }

        if (containingTypeFullName == BuilderFullName && BuilderMethods.Contains(methodName))
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

        var argumentExpression = arguments[argumentIndex].Expression;
        if (
            argumentExpression is LiteralExpressionSyntax literal
            && literal.IsKind(SyntaxKind.StringLiteralExpression)
        )
        {
            context.ReportDiagnostic(Diagnostic.Create(Rule, literal.GetLocation()));
        }
    }

    private static string GetFullTypeName(ITypeSymbol type) =>
        type.ContainingNamespace is { IsGlobalNamespace: false }
            ? $"{type.ContainingNamespace.ToDisplayString()}.{type.Name}"
            : type.Name;
}

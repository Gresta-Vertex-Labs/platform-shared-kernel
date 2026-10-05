using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SharedKernel.Analyzers.Diagnostics;

/// <summary>
/// SK0018 — Fires when a non-abstract class, record, or struct implements the open generic
/// <c>IQuery&lt;TResponse&gt;</c>, does NOT also implement <c>ICommandBase</c>, and also
/// implements <c>IInvalidatesCache</c>.
/// </summary>
/// <remarks>
/// <para>
/// Cache invalidation is commands-only by design — a pure query must never invalidate cache
/// entries as a side effect. This is the structural converse of SK0017; the two rules are
/// mutually exclusive by the "does NOT contain ICommandBase" guard — a type implementing
/// <c>ICommandBase</c> alongside <c>IQuery&lt;TResponse&gt;</c> and <c>IInvalidatesCache</c> does
/// NOT trigger SK0018 (that combination belongs to SK0017).
/// </para>
/// <para>
/// <strong>Semantic-model requirement.</strong> Same rationale as SK0017 — the full interface
/// closure (<see cref="INamedTypeSymbol.AllInterfaces"/>) must be resolved via the semantic model,
/// since these markers are typically implemented transitively. SK0018 is the domain's fourth
/// analyzer requiring this, after SK0011, SK0015, and SK0017.
/// </para>
/// <para>
/// <strong>Exemption.</strong> Types carrying the <see langword="abstract"/> modifier are
/// excluded — the same exemption already applied by SK0009/SK0017.
/// </para>
/// <para>
/// <b>Suppression:</b> use <c>#pragma warning disable SK0018</c> at the type declaration with an
/// inline comment documenting the rationale; fires globally, no suppression namespace.
/// </para>
/// <para>Introduced WO-040 P-248.</para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class QueryImplementsInvalidatesCacheAnalyzer : AnalyzerBase
{
    private const string DiagnosticId = "SK0018";
    private const string NamespacePrefix = "SharedKernel.Application";
    private const string QuerySimpleName = "IQuery";
    private const string CommandBaseSimpleName = "ICommandBase";
    private const string InvalidatesCacheSimpleName = "IInvalidatesCache";

    /// <summary>The diagnostic descriptor for SK0018.</summary>
    public static readonly DiagnosticDescriptor Rule = CreateDescriptor(
        id: DiagnosticId,
        title: "Query implements IInvalidatesCache",
        messageFormat: "'{0}' implements IQuery<TResponse> and IInvalidatesCache without also " +
                       "implementing ICommandBase. Cache invalidation is commands-only by design — " +
                       "remove IInvalidatesCache from the query type, or model the operation as a " +
                       "command instead.",
        category: Design,
        defaultSeverity: DiagnosticSeverity.Warning,
        readmeAnchor: "sk0018-queryimplementsinvalidatescache"
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
            AnalyzeTypeDeclaration,
            SyntaxKind.ClassDeclaration,
            SyntaxKind.RecordDeclaration,
            SyntaxKind.StructDeclaration
        );
    }

    private static void AnalyzeTypeDeclaration(SyntaxNodeAnalysisContext context)
    {
        var typeDecl = (TypeDeclarationSyntax)context.Node;

        if (typeDecl.Modifiers.Any(SyntaxKind.AbstractKeyword))
            return;

        if (context.SemanticModel.GetDeclaredSymbol(typeDecl, context.CancellationToken)
                is not INamedTypeSymbol symbol)
        {
            return;
        }

        var hasQuery = MarkerInterfaceHelpers.HasInterface(symbol, QuerySimpleName, arity: 1, NamespacePrefix);
        if (!hasQuery)
            return;

        var hasCommandBase = MarkerInterfaceHelpers.HasInterface(symbol, CommandBaseSimpleName, arity: 0, NamespacePrefix);
        if (hasCommandBase)
            return;

        var hasInvalidatesCache = MarkerInterfaceHelpers.HasInterface(symbol, InvalidatesCacheSimpleName, arity: 0, NamespacePrefix);
        if (!hasInvalidatesCache)
            return;

        context.ReportDiagnostic(
            Diagnostic.Create(Rule, typeDecl.Identifier.GetLocation(), symbol.Name)
        );
    }
}

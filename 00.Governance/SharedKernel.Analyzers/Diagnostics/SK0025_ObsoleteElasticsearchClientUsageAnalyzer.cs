using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SharedKernel.Analyzers.Diagnostics;

/// <summary>
/// SK0025 — bans any symbol usage (a type reference, generic-name reference, or the name in a
/// <c>using</c> directive) whose resolved <see cref="IAssemblySymbol.Name"/> is exactly
/// <c>"NEST"</c> or <c>"Elasticsearch.Net"</c>.
/// </summary>
/// <remarks>
/// <para>
/// Covers a fully-qualified <c>Nest.ElasticClient</c> reference and a bare
/// <c>ElasticClient</c>/<c>ConnectionSettings</c>/<c>QueryContainer</c> symbol usage after a
/// <c>using Nest;</c> directive, in one check — the <see cref="ISymbol.ContainingAssembly"/>-based
/// discriminator generalizes to every TYPE either deprecated package exposes without enumerating
/// them individually. Only <see cref="ITypeSymbol"/> resolutions are in scope — the namespace symbol
/// resolved for the bare <c>using Nest;</c> directive itself (or for the left-hand side of a
/// qualified name such as <c>Nest.ElasticClient</c>) is not a type usage and is never independently
/// flagged; it is the downstream type reference that fires.
/// </para>
/// <para>
/// <strong>Requires <see cref="SemanticModel"/> resolution.</strong> A syntax-only simple-name check
/// on <c>ElasticClient</c>/<c>ConnectionSettings</c> was rejected because those names are generic
/// enough to plausibly collide with unrelated types in other libraries; the exact
/// <c>ContainingAssembly.Name</c> check is the precise, collision-free discriminator, consistent
/// with SK0002's/SK0013's/SK0020's "exact declaring type/assembly, not simple name" discipline.
/// <c>Elastic.Clients.Elasticsearch</c> types resolve to a DIFFERENT
/// <c>ContainingAssembly.Name</c> ("Elastic.Clients.Elasticsearch") and therefore never trip this
/// rule.
/// </para>
/// <para>
/// No suppression namespace — fires globally, platform-wide, not scoped to <c>09.Search</c> or any
/// particular namespace: a consuming microservice adding NEST directly (not only
/// <c>SharedKernel.Search.ElasticSearch</c> itself) is exactly as unsafe. Introduced in WO-044 P-278,
/// the platform's first EOL-third-party-package prohibition rule.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ObsoleteElasticsearchClientUsageAnalyzer : AnalyzerBase
{
    private const string DiagnosticId = "SK0025";

    private const string NestAssemblyName = "NEST";
    private const string ElasticsearchNetAssemblyName = "Elasticsearch.Net";

    /// <summary>The diagnostic descriptor for SK0025.</summary>
    public static readonly DiagnosticDescriptor Rule = CreateDescriptor(
        id: DiagnosticId,
        title: "Obsolete NEST/Elasticsearch.Net client usage",
        messageFormat: "'{0}' resolves to the deprecated '{1}' package — feature-frozen since client "
            + "8.13, support window closed at end-2025. Use Elastic.Clients.Elasticsearch (the "
            + "platform-sanctioned client, pinned in SharedKernel.Search.ElasticSearch) instead.",
        category: Usage,
        defaultSeverity: DiagnosticSeverity.Warning,
        readmeAnchor: "sk0025-obsoleteelasticsearchclientusage"
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
            AnalyzeName,
            SyntaxKind.IdentifierName,
            SyntaxKind.GenericName
        );
    }

    private static void AnalyzeName(SyntaxNodeAnalysisContext context)
    {
        var nameSyntax = (SimpleNameSyntax)context.Node;

        // The "var" contextual keyword is itself an IdentifierNameSyntax whose GetSymbolInfo
        // resolves to the INFERRED type — reporting on it would duplicate the diagnostic already
        // reported on the initializer expression that actually names the deprecated type.
        if (nameSyntax.Identifier.ValueText == "var")
            return;

        var symbolInfo = context.SemanticModel.GetSymbolInfo(nameSyntax, context.CancellationToken);

        // Only TYPE references are in scope — a namespace symbol (the "Nest" in "using Nest;" or in
        // the left-hand side of the qualified name "Nest.ElasticClient") is not itself a usage of a
        // deprecated client type and must not be independently flagged; the actual type reference
        // (e.g. "ElasticClient") is what fires.
        if (symbolInfo.Symbol is not ITypeSymbol symbol)
            return;

        var assemblyName = symbol.ContainingAssembly?.Name;
        if (assemblyName is NestAssemblyName or ElasticsearchNetAssemblyName)
        {
            context.ReportDiagnostic(
                Diagnostic.Create(Rule, nameSyntax.GetLocation(), nameSyntax.Identifier.Text, assemblyName)
            );
        }
    }
}

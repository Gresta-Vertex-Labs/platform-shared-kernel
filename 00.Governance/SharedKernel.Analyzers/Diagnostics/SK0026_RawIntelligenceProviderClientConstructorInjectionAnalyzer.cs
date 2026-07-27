using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SharedKernel.Analyzers.Diagnostics;

/// <summary>
/// SK0026 — Fires when a constructor declares a parameter whose type resolves (via
/// <see cref="SemanticModel"/>) to exactly <c>Qdrant.Client.QdrantClient</c> or
/// <c>Microsoft.SemanticKernel.Kernel</c>, unless the parameter's enclosing type sits inside that
/// client's own owning provider package's namespace.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Two-provider client set (WO-048 adaptation):</strong> the design originally scoped for
/// this rule (WO-045 P-286) named three client types — <c>Qdrant.Client.QdrantClient</c>,
/// <c>Milvus.Client.MilvusClient</c>, and <c>Microsoft.SemanticKernel.Kernel</c> — matching
/// <c>10.Intelligence</c>'s originally-planned three sibling providers. <c>SharedKernel.AI.Milvus</c>
/// was permanently retracted before ever being built (WO-048, 2026-07-27 — <c>Milvus.Client</c> never
/// shipped a stable release) — the platform no longer recognizes Milvus as a provider, so this
/// analyzer resolves only the two client types that correspond to the two providers that actually
/// ship: <c>Qdrant.Client.QdrantClient</c> (exempt inside <c>SharedKernel.AI.Qdrant</c>) and
/// <c>Microsoft.SemanticKernel.Kernel</c> (exempt inside <c>SharedKernel.AI.SemanticKernel</c>).
/// </para>
/// <para>
/// Requires <see cref="SemanticModel.GetTypeInfo(SyntaxNode, System.Threading.CancellationToken)"/>
/// exact-full-type-name resolution — not the syntax-only simple-name check SK0013 uses for
/// <c>HttpClient</c> — because <c>Microsoft.SemanticKernel.Kernel</c>'s simple name <c>"Kernel"</c>
/// is highly collision-prone (a convolution kernel, an OS-kernel abstraction, a compute-shader kernel
/// are all plausible unrelated types named <c>Kernel</c> elsewhere in a consuming service's own
/// dependency graph); <c>QdrantClient</c> alone would likely be safe as a syntax-only check, but both
/// are resolved uniformly via the semantic model for implementation consistency.
/// </para>
/// <para>
/// <strong>Namespace exemption is per-client-type, not shared:</strong> a <c>QdrantClient</c>
/// parameter is exempt only inside a namespace starting with <c>SharedKernel.AI.Qdrant</c>; a
/// <c>Kernel</c> parameter is exempt only inside a namespace starting with
/// <c>SharedKernel.AI.SemanticKernel</c>. There is no cross-exemption — a <c>QdrantClient</c>
/// parameter injected inside <c>SharedKernel.AI.SemanticKernel</c> still fires, since that would
/// itself be exactly the sibling-package violation
/// <see cref="ArchitectureTests.Rules.IntelligenceTopologyRules.ProviderPackagesNeverReferenceEachOther"/>
/// independently catches at the assembly level.
/// </para>
/// <para>
/// <strong>Suppression:</strong> per-constructor via <c>#pragma warning disable SK0026</c>; document
/// the rationale inline.
/// </para>
/// <para>Introduced WO-045 P-286, the platform's first <c>10.Intelligence</c>-domain diagnostic
/// alongside SK0027. This domain's tenth semantic-model analyzer (after SK0011, SK0015,
/// SK0017–SK0019, SK0020, SK0022, SK0024, SK0025).</para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class RawIntelligenceProviderClientConstructorInjectionAnalyzer : AnalyzerBase
{
    private const string DiagnosticId = "SK0026";

    private const string QdrantClientFullName = "Qdrant.Client.QdrantClient";
    private const string SemanticKernelKernelFullName = "Microsoft.SemanticKernel.Kernel";

    private const string QdrantOwningNamespacePrefix = "SharedKernel.AI.Qdrant";
    private const string SemanticKernelOwningNamespacePrefix = "SharedKernel.AI.SemanticKernel";

    /// <summary>
    /// Maps each recognized raw provider-client full type name to the namespace prefix that
    /// exempts it — the two-provider set surviving the WO-048 Milvus retraction.
    /// </summary>
    private static readonly Dictionary<string, string> ClientTypeToOwningNamespace =
        new(StringComparer.Ordinal)
        {
            [QdrantClientFullName] = QdrantOwningNamespacePrefix,
            [SemanticKernelKernelFullName] = SemanticKernelOwningNamespacePrefix,
        };

    /// <summary>The diagnostic descriptor for SK0026.</summary>
    public static readonly DiagnosticDescriptor Rule = CreateDescriptor(
        id: DiagnosticId,
        title: "Raw vector-DB/model-SDK client injected outside its owning provider package",
        messageFormat: "Constructor parameter '{0}' is typed as the raw provider client '{1}'. "
            + "Inject the neutral SharedKernel.AI.Abstractions contracts instead "
            + "(IVectorCollection<TRecord>, IEmbeddingGenerator, ISemanticKernel, "
            + "IVectorCollectionProvisioner, IVectorProviderDescriptor, ICompletionProviderDescriptor). "
            + "A raw-client escape hatch, if genuinely required, must follow the platform's "
            + "three-gate pattern (opt-in builder call, startup Warning, governance architecture "
            + "test) and its XML doc must state IN CAPITALS that it bypasses tenant scoping.",
        category: Usage,
        defaultSeverity: DiagnosticSeverity.Warning,
        readmeAnchor: "sk0026-rawintelligenceproviderclientconstructorinjection"
    );

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSyntaxNodeAction(AnalyzeConstructor, SyntaxKind.ConstructorDeclaration);
    }

    private static void AnalyzeConstructor(SyntaxNodeAnalysisContext context)
    {
        var ctorDecl = (ConstructorDeclarationSyntax)context.Node;

        foreach (var parameter in ctorDecl.ParameterList.Parameters)
        {
            if (parameter.Type is null)
                continue;

            var typeInfo = context.SemanticModel.GetTypeInfo(parameter.Type, context.CancellationToken);
            var parameterType = typeInfo.Type;
            if (parameterType is null)
                continue;

            var fullTypeName = GetFullTypeName(parameterType);
            if (!ClientTypeToOwningNamespace.TryGetValue(fullTypeName, out var owningNamespacePrefix))
                continue;

            if (IsInsideNamespace(ctorDecl, owningNamespacePrefix))
                continue;

            context.ReportDiagnostic(
                Diagnostic.Create(
                    Rule,
                    parameter.Type.GetLocation(),
                    parameter.Identifier.Text,
                    fullTypeName
                )
            );
        }
    }

    /// <summary>
    /// Walks ancestor syntax nodes looking for a namespace declaration whose qualified name starts
    /// with <paramref name="namespacePrefix"/>. Same pattern as SK0001/SK0007/SK0013.
    /// </summary>
    private static bool IsInsideNamespace(SyntaxNode node, string namespacePrefix)
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

            if (nsName is not null && nsName.StartsWith(namespacePrefix, StringComparison.Ordinal))
                return true;

            current = current.Parent;
        }

        return false;
    }

    private static string GetFullTypeName(ITypeSymbol type) =>
        type.ContainingNamespace is { IsGlobalNamespace: false }
            ? $"{type.ContainingNamespace.ToDisplayString()}.{type.Name}"
            : type.Name;
}

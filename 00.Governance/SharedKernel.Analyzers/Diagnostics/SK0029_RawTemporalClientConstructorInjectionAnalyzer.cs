using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SharedKernel.Analyzers.Diagnostics;

/// <summary>
/// SK0029 — Fires when a constructor declares a parameter whose type resolves (via
/// <see cref="SemanticModel"/>) to exactly one of four raw Temporal SDK types —
/// <c>Temporalio.Client.ITemporalClient</c>, <c>Temporalio.Client.TemporalClient</c>,
/// <c>Temporalio.Worker.TemporalWorker</c>, or <c>Temporalio.Client.WorkflowHandle</c> (any generic
/// arity) — unless the parameter's enclosing type sits inside a namespace starting with
/// <c>SharedKernel.Workflows.Temporal</c>.
/// </summary>
/// <remarks>
/// <para>
/// A SINGLE shared exemption namespace prefix — structurally closer to SK0013's
/// one-namespace-exemption shape than to SK0026's per-client-type/per-package exemption mapping,
/// because <c>17.Workflows</c> has exactly one owning package, not three siblings.
/// </para>
/// <para>
/// <strong>Fix:</strong> inject <c>IWorkflowDispatcher</c> (to start/signal/query workflows) or
/// <c>IWorkflowHandle</c> (to interact with an already-started execution) instead — both from
/// <c>SharedKernel.Workflows.Temporal</c>'s own abstraction surface. If a genuine Visibility-API/
/// schedule/namespace-administration/Nexus need remains unmet by either, the sanctioned path is the
/// three-gate <c>ITemporalRawClientAccessor</c> escape hatch (composition-root
/// <c>.AllowRawClientAccess()</c> opt-in, a startup <c>Warning</c>, and
/// <see cref="ArchitectureTests.Rules.WorkflowTopologyRules.NoRawClientAccessorConsumptionInRepo"/>) —
/// never a raw constructor-injected <c>Temporalio.*</c> client type.
/// </para>
/// <para>
/// <strong>Suppression:</strong> per-constructor via <c>#pragma warning disable SK0029</c>; document
/// the rationale inline — no legitimate use case outside <c>SharedKernel.Workflows.Temporal</c>
/// itself is known.
/// </para>
/// <para>Introduced WO-046 P-290, structurally identical to SK0013 (raw <c>HttpClient</c>, P-159) and
/// SK0026 (raw vector-DB/model-SDK client, P-286) — the platform's third instance of the "raw
/// third-party client injected outside its owning abstraction package" prohibition shape. This
/// domain's thirteenth semantic-model analyzer (after SK0028).</para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class RawTemporalClientConstructorInjectionAnalyzer : AnalyzerBase
{
    private const string DiagnosticId = "SK0029";

    private const string ITemporalClientFullName = "Temporalio.Client.ITemporalClient";
    private const string TemporalClientFullName = "Temporalio.Client.TemporalClient";
    private const string TemporalWorkerFullName = "Temporalio.Worker.TemporalWorker";
    private const string WorkflowHandleSimpleName = "WorkflowHandle";
    private const string WorkflowHandleNamespace = "Temporalio.Client";

    private const string OwningNamespacePrefix = "SharedKernel.Workflows.Temporal";

    /// <summary>The diagnostic descriptor for SK0029.</summary>
    public static readonly DiagnosticDescriptor Rule = CreateDescriptor(
        id: DiagnosticId,
        title: "Raw Temporal client type injected outside SharedKernel.Workflows.Temporal",
        messageFormat: "Constructor parameter '{0}' is typed as the raw Temporal type '{1}'. Inject "
            + "IWorkflowDispatcher (to start/signal/query workflows) or IWorkflowHandle (to interact "
            + "with an already-started execution) instead. If a genuine Visibility-API/schedule/"
            + "namespace-administration/Nexus need remains unmet, use the three-gate "
            + "ITemporalRawClientAccessor escape hatch instead of a raw constructor-injected client.",
        category: Usage,
        defaultSeverity: DiagnosticSeverity.Warning,
        readmeAnchor: "sk0029-rawtemporalclientconstructorinjection"
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
            if (parameter.Type is not { } parameterTypeSyntax)
                continue;

            var typeInfo = context.SemanticModel.GetTypeInfo(parameterTypeSyntax, context.CancellationToken);
            if (typeInfo.Type is not { } parameterType)
                continue;

            if (!TryGetMatchedRawTypeName(parameterType, out var matchedTypeName))
                continue;

            if (IsInsideNamespace(ctorDecl, OwningNamespacePrefix))
                continue;

            context.ReportDiagnostic(
                Diagnostic.Create(
                    Rule,
                    parameterTypeSyntax.GetLocation(),
                    parameter.Identifier.Text,
                    matchedTypeName
                )
            );
        }
    }

    private static bool TryGetMatchedRawTypeName(ITypeSymbol parameterType, out string matchedTypeName)
    {
        var fullName = GetFullTypeName(parameterType);

        if (fullName is ITemporalClientFullName or TemporalClientFullName or TemporalWorkerFullName)
        {
            matchedTypeName = fullName;
            return true;
        }

        if (
            parameterType is INamedTypeSymbol { Name: WorkflowHandleSimpleName } namedType
            && namedType.ContainingNamespace?.ToDisplayString() == WorkflowHandleNamespace
        )
        {
            matchedTypeName = $"{WorkflowHandleNamespace}.{WorkflowHandleSimpleName}";
            return true;
        }

        matchedTypeName = string.Empty;
        return false;
    }

    /// <summary>
    /// Walks ancestor syntax nodes looking for a namespace declaration whose qualified name starts
    /// with <paramref name="namespacePrefix"/>. Same pattern as SK0001/SK0007/SK0013/SK0026.
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

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SharedKernel.Analyzers.Diagnostics;

/// <summary>
/// SK0036 — Fires on a <c>new RpcException(...)</c> or standalone <c>new Status(...)</c>
/// construction whose constructed type resolves, by exact semantic-model match, to
/// <c>Grpc.Core.RpcException</c>/<c>Grpc.Core.Status</c>, anywhere outside the
/// <c>SharedKernel.Presentation.Grpc</c> namespace.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Exact-type resolution, not a syntax-only simple-name match.</strong> "Status" is a
/// dangerously generic simple name elsewhere on this platform and in consuming services (order
/// status, application status, health-check status enums, ...) — the exact lesson SK0026 already
/// recorded for "Kernel." SK0013's "HttpClient is unique enough to match syntactically" precedent
/// does not transfer here. Every constructed type is confirmed via
/// <see cref="SemanticModel.GetTypeInfo(SyntaxNode, System.Threading.CancellationToken)"/> to
/// resolve to exactly <c>Grpc.Core.RpcException</c> or <c>Grpc.Core.Status</c> before the diagnostic
/// fires.
/// </para>
/// <para>
/// <strong>Two constructed-type checks, one diagnostic ID</strong> — a bare <c>new Status(...)</c>
/// may be built for later use (e.g. assigned to a trailer) without being immediately wrapped in an
/// <c>RpcException</c>, so both shapes are flagged, mirroring SK0022's/SK0024's established
/// multi-shape-one-ID precedent.
/// </para>
/// <para>
/// <strong>Single shared exemption namespace prefix</strong> — <c>SharedKernel.Presentation.Grpc</c>
/// (mirrors SK0029's one-owning-package shape, not SK0026's per-client-type mapping: gRPC server
/// error mapping is one owning package). This single prefix already covers every real, sanctioned
/// construction site in that package — the canonical mapping path
/// (<c>SharedKernel.Presentation.Grpc.GrpcResultExtensions</c>: <c>ThrowIfFailure()</c> and
/// <c>GetValueOrThrow()</c>), the rich-status factory behind it
/// (<c>SharedKernel.Presentation.Grpc.Errors.RpcStatusFactory</c>) and the exception interceptor that maps a thrown
/// exception (<c>SharedKernel.Presentation.Grpc.Interceptors.GrpcExceptionInterceptor</c>) all live in
/// <c>SharedKernel.Presentation.Grpc</c> or its sub-namespaces, confirmed against the real package — no
/// per-file exemption list is needed. The P-562 redesign moved the result extensions from
/// <c>…Grpc.Results</c> to the root namespace, renamed <c>ToGrpcResult</c> to <c>ThrowIfFailure</c>/
/// <c>GetValueOrThrow</c> and deleted the authorization interceptor; the prefix covers the new layout unchanged,
/// so the rule's behavior did not change — only its message.
/// </para>
/// <para>
/// Fires globally outside that namespace — no "must be inside a gRPC service method" scoping is
/// needed structurally, since <c>RpcException</c>/<c>Status</c> are gRPC-specific types by
/// construction; any code constructing them is gRPC-adjacent code by definition. Introduced WO-074
/// P-469. UNGATED — needs no compiled reference to <c>SharedKernel.Presentation.Grpc</c> itself (a
/// namespace-string exemption match needs no <c>ProjectReference</c>) and no dependency on
/// <c>Grpc.AspNetCore</c>; only <c>SharedKernel.Analyzers.Tests</c> needs a test-only
/// <c>PackageReference</c> to the lightweight, standalone <c>Grpc.Core.Api</c> package (containing
/// solely <c>RpcException</c>/<c>Status</c>/<c>StatusCode</c>, not the full server hosting stack)
/// for fixture compilation. No <c>SharedKernel.ArchitectureTests</c> counterpart, mirroring SK0013's
/// own shape — a construction-path prohibition needs no companion IL-level regression lock beyond
/// the Roslyn analyzer itself.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class RawRpcExceptionConstructionAnalyzer : AnalyzerBase
{
    private const string DiagnosticId = "SK0036";
    private const string RpcExceptionTypeName = "RpcException";
    private const string StatusTypeName = "Status";
    private const string GrpcCoreNamespace = "Grpc.Core";
    private const string ExemptedNamespacePrefix = "SharedKernel.Presentation.Grpc";

    /// <summary>The diagnostic descriptor for SK0036.</summary>
    public static readonly DiagnosticDescriptor Rule = CreateDescriptor(
        id: DiagnosticId,
        title: "Raw RpcException/Status construction outside SharedKernel.Presentation.Grpc",
        messageFormat: "Direct construction of Grpc.Core.{0} is prohibited outside "
            + "SharedKernel.Presentation.Grpc. Return a Result and call "
            + "SharedKernel.Presentation.Grpc.GrpcResultExtensions.ThrowIfFailure()/"
            + ".GetValueOrThrow() to map a failure to an RpcException instead of "
            + "hand-constructing one.",
        category: Usage,
        defaultSeverity: DiagnosticSeverity.Warning,
        readmeAnchor: "sk0036-rawrpcexceptionconstruction"
    );

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSyntaxNodeAction(AnalyzeObjectCreation, SyntaxKind.ObjectCreationExpression);
    }

    private static void AnalyzeObjectCreation(SyntaxNodeAnalysisContext context)
    {
        var objectCreation = (ObjectCreationExpressionSyntax)context.Node;

        if (IsInsideExemptedNamespace(objectCreation))
            return;

        var typeInfo = context.SemanticModel.GetTypeInfo(objectCreation, context.CancellationToken);
        var constructedType = typeInfo.Type;

        if (!IsGrpcCoreType(constructedType, RpcExceptionTypeName)
            && !IsGrpcCoreType(constructedType, StatusTypeName))
        {
            return;
        }

        context.ReportDiagnostic(
            Diagnostic.Create(Rule, objectCreation.GetLocation(), constructedType!.Name)
        );
    }

    private static bool IsGrpcCoreType(ITypeSymbol? type, string expectedSimpleName) =>
        type is not null
        && type.Name == expectedSimpleName
        && type.ContainingNamespace is not null
        && type.ContainingNamespace.ToDisplayString() == GrpcCoreNamespace;

    /// <summary>
    /// Walks ancestor syntax nodes looking for a namespace declaration whose qualified name starts
    /// with <see cref="ExemptedNamespacePrefix"/>. Same pattern as SK0001/SK0007/SK0013/SK0026/
    /// SK0029/SK0031.
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

            if (nsName is not null
                && nsName.StartsWith(ExemptedNamespacePrefix, System.StringComparison.Ordinal))
            {
                return true;
            }

            current = current.Parent;
        }

        return false;
    }
}

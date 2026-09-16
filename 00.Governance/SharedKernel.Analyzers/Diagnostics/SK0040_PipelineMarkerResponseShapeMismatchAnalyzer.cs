using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SharedKernel.Analyzers.Diagnostics;

/// <summary>
/// SK0040 — Fires when a non-abstract class, record, or struct implements a
/// <c>SharedKernel.Application.Behaviors</c> marker interface whose owning behavior
/// short-circuits by constructing a failed response through the internal
/// <c>FailureResponse.Create&lt;TResponse&gt;</c> helper, while also implementing
/// <c>MediatR.IRequest&lt;TResponse&gt;</c> with a <c>TResponse</c> that is neither the
/// non-generic <c>Result</c> nor a closed <c>Result&lt;T&gt;</c>.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The gap.</strong> <c>FailureResponse.Create&lt;TResponse&gt;</c>
/// (<c>SharedKernel.Application.Behaviors/Shared/FailureResponse.cs</c>) binds to a public static
/// <c>Failure(Error)</c> factory resolved via reflection the first time a closed
/// <c>TResponse</c> is used. <c>Result</c> takes a hardcoded fast path; every other
/// <c>TResponse</c> must expose that factory or the call throws
/// <see cref="InvalidOperationException"/> — at runtime, on the first authorization denial,
/// duplicate submission, or validation failure, never at compile time. This rule moves that
/// failure to compile time.
/// </para>
/// <para>
/// <strong>Only the markers whose behavior genuinely calls <c>FailureResponse.Create</c> are
/// checked.</strong> Reading <c>FailureResponse.cs</c> and every behavior that references it
/// found exactly two: <c>Authorization.AuthorizationBehavior{TRequest,TResponse}</c> (gated by
/// <c>Authorization.IAuthorizeRequest</c>) and <c>Idempotency.IdempotencyBehavior{TRequest,TResponse}</c>
/// (gated by <c>Idempotency.IIdempotentRequest</c>). <c>Auditing.AuditingBehavior{TRequest,TResponse}</c>
/// (gated by <c>Auditing.IAuditableRequest{TResponse}</c>) and <c>Logging.LoggingBehavior{TRequest,TResponse}</c>
/// (gated by <c>Logging.ILoggableRequest{TResponse}</c>) never call it — both only ever forward
/// the response <c>next()</c> already produced and read it through
/// <c>Shared.ResponseOutcome.TryGetError</c>, which degrades gracefully (treats a non-<c>Result</c>
/// response as a success) rather than requiring a <c>Failure(Error)</c> factory. Implementing
/// either of those two markers on a plain-DTO-response request compiles and runs without ever
/// throwing — so this rule deliberately does not check them. <c>ValidationBehavior</c> also calls
/// <c>FailureResponse.Create</c>, but it applies to every <c>TRequest : IRequest&lt;TResponse&gt;</c>
/// unconditionally — there is no marker interface to gate scope on, so it is out of reach for a
/// type-declaration rule of this shape.
/// </para>
/// <para>
/// <strong>Semantic-model requirement.</strong> Both markers, and <c>MediatR.IRequest&lt;TResponse&gt;</c>
/// itself, are typically implemented transitively (through <c>ICommand&lt;TResponse&gt;</c>/
/// <c>IQuery&lt;TResponse&gt;</c>), so the full interface closure
/// (<see cref="INamedTypeSymbol.AllInterfaces"/>) must be resolved via the semantic model —
/// the same requirement already established by SK0011, SK0015, and SK0017–SK0019.
/// </para>
/// <para>
/// <strong>What counts as Result-shaped.</strong> The resolved <c>TResponse</c> passes when it is
/// an <see cref="INamedTypeSymbol"/> named <c>Result</c>, of arity 0 or 1, whose containing
/// namespace is exactly <c>SharedKernel.Primitives.Results</c> — matching <c>FailureResponse.cs</c>'s
/// own fast path (<c>Result</c>) and its reflection fallback (any type exposing a static
/// <c>Failure(Error)</c> factory, which every <c>Result&lt;T&gt;</c> does). A closed
/// <c>Result&lt;T&gt;</c> passes regardless of whether its own type argument <c>T</c> is itself
/// still an open type parameter — only the outer <c>Result</c>/<c>Result&lt;T&gt;</c> shape is
/// checked, never the payload.
/// </para>
/// <para>
/// <strong>Open/unresolved response types are never flagged.</strong> When the resolved
/// <c>TResponse</c> is itself a type parameter (e.g. a generic request class declaring
/// <c>IRequest&lt;TResult&gt;</c> against its own open <c>TResult</c>) or an unresolved/error
/// type, this rule cannot determine the eventual closed shape and does not report — a false
/// negative accepted deliberately rather than risk a false positive against a generic request
/// base a consuming service will only ever close with <c>Result</c>/<c>Result&lt;T&gt;</c>.
/// </para>
/// <para>
/// <strong>No <c>IRequest&lt;TResponse&gt;</c>, no diagnostic.</strong> A type implementing one of
/// the two markers without implementing <c>MediatR.IRequest&lt;TResponse&gt;</c> at all can never
/// have either behavior resolve into its pipeline (MediatR's own DI resolution requires it), so
/// there is no runtime hazard and this rule does not report.
/// </para>
/// <para>
/// <strong>Exemption.</strong> Types carrying the <see langword="abstract"/> modifier are
/// excluded — the same exemption already applied by SK0009/SK0017/SK0018.
/// </para>
/// <para>
/// <b>Suppression:</b> use <c>#pragma warning disable SK0040</c> at the type declaration with an
/// inline comment documenting the rationale; fires globally, no suppression namespace.
/// </para>
/// <para>Introduced as a governance companion to <c>05.Application</c>'s P-544 pre-publish redesign.</para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class PipelineMarkerResponseShapeMismatchAnalyzer : AnalyzerBase
{
    private const string DiagnosticId = "SK0040";
    private const string NamespacePrefix = "SharedKernel.Application";
    private const string ResultsNamespace = "SharedKernel.Primitives.Results";
    private const string MediatRNamespace = "MediatR";
    private const string RequestSimpleName = "IRequest";
    private const string ResultSimpleName = "Result";

    private static readonly (string SimpleName, int Arity)[] FailureConstructingMarkers =
    [
        ("IAuthorizeRequest", 0),
        ("IIdempotentRequest", 0),
    ];

    /// <summary>The diagnostic descriptor for SK0040.</summary>
    public static readonly DiagnosticDescriptor Rule = CreateDescriptor(
        id: DiagnosticId,
        title: "Pipeline marker interface requires a Result-shaped response",
        messageFormat: "'{0}' implements {1}, which short-circuits with a failed response via "
            + "FailureResponse.Create<TResponse> — but its MediatR response type is '{2}', not "
            + "Result or a closed Result<T>. This throws InvalidOperationException the first time "
            + "the behavior short-circuits, at runtime. Declare the response as Result or "
            + "Result<T>, or remove {1}.",
        category: Design,
        defaultSeverity: DiagnosticSeverity.Warning,
        readmeAnchor: "sk0040-pipelinemarkerresponseshapemismatch"
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

        var matchedMarkers = CollectMatchedMarkers(symbol);
        if (matchedMarkers.Count == 0)
            return;

        var responseType = TryGetMediatRResponseType(symbol);
        if (responseType is null)
            return;

        if (responseType.TypeKind is TypeKind.TypeParameter or TypeKind.Error)
            return;

        if (IsResultShaped(responseType))
            return;

        context.ReportDiagnostic(
            Diagnostic.Create(
                Rule,
                typeDecl.Identifier.GetLocation(),
                symbol.Name,
                string.Join(" and ", matchedMarkers),
                responseType.ToDisplayString()
            )
        );
    }

    private static List<string> CollectMatchedMarkers(INamedTypeSymbol symbol)
    {
        var matched = new List<string>(FailureConstructingMarkers.Length);

        foreach (var (simpleName, arity) in FailureConstructingMarkers)
        {
            if (MarkerInterfaceHelpers.HasInterface(symbol, simpleName, arity, NamespacePrefix))
                matched.Add(simpleName);
        }

        return matched;
    }

    private static ITypeSymbol? TryGetMediatRResponseType(INamedTypeSymbol symbol)
    {
        foreach (var iface in symbol.AllInterfaces)
        {
            var original = iface.OriginalDefinition;

            if (original.Arity != 1 || original.Name != RequestSimpleName)
                continue;

            var ns = original.ContainingNamespace;
            if (ns is null || ns.IsGlobalNamespace || ns.ToDisplayString() != MediatRNamespace)
                continue;

            return iface.TypeArguments[0];
        }

        return null;
    }

    private static bool IsResultShaped(ITypeSymbol responseType)
    {
        if (responseType is not INamedTypeSymbol named)
            return false;

        if (named.Name != ResultSimpleName || (named.Arity != 0 && named.Arity != 1))
            return false;

        var ns = named.ContainingNamespace;
        if (ns is null || ns.IsGlobalNamespace)
            return false;

        return ns.ToDisplayString() == ResultsNamespace;
    }
}

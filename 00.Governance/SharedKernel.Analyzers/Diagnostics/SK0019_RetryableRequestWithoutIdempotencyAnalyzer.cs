using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SharedKernel.Analyzers.Diagnostics;

/// <summary>
/// SK0019 — Fires when a non-abstract class, record, or struct implements
/// <c>IRetryableRequest</c> without also implementing <c>IIdempotentRequest</c>.
/// </summary>
/// <remarks>
/// <para>
/// Closes 05.Application/CLAUDE.md's own documented, explicitly-accepted "not mechanically
/// enforced" gap for the <c>IRetryableRequest</c>/<c>IIdempotentRequest</c> pairing — a retried
/// request that already partially committed on the first attempt is otherwise re-executed
/// instead of returning the original outcome, unless <c>IdempotentCommandBehavior</c> can guard
/// against it via <c>IIdempotentRequest</c>.
/// </para>
/// <para>
/// <strong>Semantic-model requirement.</strong> Same rationale as SK0017/SK0018 — the full
/// interface closure (<see cref="INamedTypeSymbol.AllInterfaces"/>) must be resolved via the
/// semantic model. SK0019 is the domain's fifth analyzer requiring this, after SK0011, SK0015,
/// SK0017, and SK0018.
/// </para>
/// <para>
/// <strong>Exemption.</strong> Types carrying the <see langword="abstract"/> modifier are
/// excluded — the same exemption already applied by SK0009/SK0017/SK0018.
/// </para>
/// <para>
/// <b>Suppression:</b> use <c>#pragma warning disable SK0019</c> at the type declaration with an
/// inline comment documenting the rationale (e.g., an idempotency-key store is provided
/// out-of-band); fires globally, no suppression namespace.
/// </para>
/// <para>Introduced WO-040 P-248.</para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class RetryableRequestWithoutIdempotencyAnalyzer : AnalyzerBase
{
    private const string DiagnosticId = "SK0019";
    private const string NamespacePrefix = "SharedKernel.Application";
    private const string RetryableRequestSimpleName = "IRetryableRequest";
    private const string IdempotentRequestSimpleName = "IIdempotentRequest";

    /// <summary>The diagnostic descriptor for SK0019.</summary>
    public static readonly DiagnosticDescriptor Rule = CreateDescriptor(
        id: DiagnosticId,
        title: "Retryable request without idempotency guard",
        messageFormat: "'{0}' implements IRetryableRequest without also implementing " +
                       "IIdempotentRequest. Implement IIdempotentRequest so IdempotentCommandBehavior " +
                       "can guard against the retry-after-partial-commit hazard, or remove " +
                       "IRetryableRequest if idempotency truly cannot be guaranteed.",
        category: Design,
        defaultSeverity: DiagnosticSeverity.Warning,
        readmeAnchor: "sk0019-retryablerequestwithoutidempotency"
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

        var hasRetryable = MarkerInterfaceHelpers.HasInterface(symbol, RetryableRequestSimpleName, arity: 0, NamespacePrefix);
        if (!hasRetryable)
            return;

        var hasIdempotent = MarkerInterfaceHelpers.HasInterface(symbol, IdempotentRequestSimpleName, arity: 0, NamespacePrefix);
        if (hasIdempotent)
            return;

        context.ReportDiagnostic(
            Diagnostic.Create(Rule, typeDecl.Identifier.GetLocation(), symbol.Name)
        );
    }
}

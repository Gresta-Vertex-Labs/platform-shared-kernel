using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SharedKernel.Analyzers.Diagnostics;

/// <summary>
/// SK0017 — Fires when a non-abstract class, record, or struct implements both
/// <c>ICommandBase</c> and the open generic <c>ICacheableQuery&lt;TResponse&gt;</c>
/// (directly or transitively through a narrower interface).
/// </summary>
/// <remarks>
/// <para>
/// Caching is queries-only by design — a command must never be cacheable. This rule runs
/// inside a CONSUMING microservice's own compilation; the violation is a command/query type
/// declaration, which never occurs inside <c>SharedKernel.Application.Behaviors</c> itself.
/// </para>
/// <para>
/// <strong>Semantic-model requirement.</strong> <c>ICommandBase</c> is typically implemented
/// transitively (e.g. through <c>ICommand&lt;TResponse&gt; : ICommandBase</c>), so a
/// <see cref="BaseListSyntax"/> simple-name check is insufficient — the full interface closure
/// (<see cref="INamedTypeSymbol.AllInterfaces"/>) must be resolved via the semantic model. SK0017
/// is the domain's third analyzer requiring this, after SK0011 and SK0015.
/// </para>
/// <para>
/// Interface matching uses <see cref="INamedTypeSymbol.OriginalDefinition"/> plus a
/// <c>"SharedKernel.Application"</c> containing-namespace prefix check (covering both
/// <c>SharedKernel.Application</c> and <c>SharedKernel.Application.Behaviors</c>) rather than
/// exact-assembly identity — this keeps analyzer test fixtures self-contained, with no
/// <c>ProjectReference</c> to the real assemblies required.
/// </para>
/// <para>
/// <strong>Exemption.</strong> Types carrying the <see langword="abstract"/> modifier are
/// excluded — the same exemption already applied by SK0009.
/// </para>
/// <para>
/// <b>Suppression:</b> use <c>#pragma warning disable SK0017</c> at the type declaration with an
/// inline comment documenting the rationale; fires globally, no suppression namespace.
/// </para>
/// <para>Introduced WO-040 P-248.</para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class CommandImplementsCacheableQueryAnalyzer : AnalyzerBase
{
    private const string DiagnosticId = "SK0017";
    private const string NamespacePrefix = "SharedKernel.Application";
    private const string CommandBaseSimpleName = "ICommandBase";
    private const string CacheableQuerySimpleName = "ICacheableQuery";

    /// <summary>The diagnostic descriptor for SK0017.</summary>
    public static readonly DiagnosticDescriptor Rule = CreateDescriptor(
        id: DiagnosticId,
        title: "Command implements ICacheableQuery<TResponse>",
        messageFormat: "'{0}' implements both ICommandBase and ICacheableQuery<TResponse>. " +
                       "Caching is queries-only by design — remove ICacheableQuery<TResponse> " +
                       "from the command type, or model the operation as a query instead.",
        category: Design,
        defaultSeverity: DiagnosticSeverity.Warning,
        readmeAnchor: "sk0017-commandimplementscacheablequery"
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

        var hasCommandBase = MarkerInterfaceHelpers.HasInterface(
            symbol,
            CommandBaseSimpleName,
            arity: 0,
            NamespacePrefix
        );
        if (!hasCommandBase)
            return;

        var hasCacheableQuery = MarkerInterfaceHelpers.HasInterface(
            symbol,
            CacheableQuerySimpleName,
            arity: 1,
            NamespacePrefix
        );
        if (!hasCacheableQuery)
            return;

        context.ReportDiagnostic(
            Diagnostic.Create(Rule, typeDecl.Identifier.GetLocation(), symbol.Name)
        );
    }
}

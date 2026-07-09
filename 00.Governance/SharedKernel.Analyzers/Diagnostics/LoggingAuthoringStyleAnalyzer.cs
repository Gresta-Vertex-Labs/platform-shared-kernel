using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SharedKernel.Analyzers.Diagnostics;

/// <summary>
/// SK0020 / SK0021 — bans the two hand-rolled logging authoring styles the platform's
/// <c>[LoggerMessage]</c>-only logging standard (root <c>CLAUDE.md</c> "Logging Conventions",
/// WO-041 P-249/P-250) prohibits: direct <c>ILogger</c> extension-method calls (SK0020) and
/// hand-written <c>LoggerMessage.Define*</c> delegate construction (SK0021).
/// </summary>
/// <remarks>
/// <para>
/// This is the first two-diagnostics-one-analyzer-class shape in this domain — every prior SK
/// analyzer was one class per diagnostic ID. Both diagnostics encode the same platform logging
/// standard ("always <c>[LoggerMessage]</c>, never hand-rolled") and share two guards: the
/// <c>SharedKernel.Testing</c> namespace exemption and the
/// <see cref="GeneratedCodeAnalysisFlags.None"/> generated-code guard.
/// </para>
/// <para>
/// <strong>SK0020 — DirectILoggerExtensionMethodUsage.</strong> Requires
/// <see cref="SemanticModel.GetSymbolInfo(SyntaxNode, System.Threading.CancellationToken)"/> on
/// the invoked method, checking <c>ContainingType</c> for an EXACT match against
/// <c>Microsoft.Extensions.Logging.LoggerExtensions</c> (name starting with <c>"Log"</c>) or
/// <c>Microsoft.Extensions.Logging.ILogger</c> (name exactly <c>"Log"</c>). A syntax-only
/// simple-name check was rejected — <c>LogInformation</c>/<c>LogWarning</c>/etc. collide with
/// unrelated logging frameworks (Serilog's own <c>ILogger</c>, NLog, custom wrapper types) that
/// may coexist in a consuming microservice's dependency tree.
/// </para>
/// <para>
/// <strong>SK0021 — HandWrittenLoggerMessageDefineDelegate.</strong> Syntax-only:
/// <see cref="InvocationExpressionSyntax"/> whose <c>Expression</c> is a
/// <see cref="MemberAccessExpressionSyntax"/> with a qualifier identifier text of
/// <c>"LoggerMessage"</c> and a name identifier text starting with <c>"Define"</c> (covers
/// <c>Define</c>/<c>DefineScope</c> across all generic arities). No semantic model — the
/// qualified <c>LoggerMessage.Define*</c> call shape is specific enough that the cost of a
/// symbol resolution is not justified.
/// </para>
/// <para>
/// <strong>Generated-code guard (load-bearing):</strong> this analyzer calls
/// <see cref="AnalysisContext.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags)"/> with
/// <see cref="GeneratedCodeAnalysisFlags.None"/> before registering any syntax-node action.
/// <c>[LoggerMessage]</c>'s own source generator emits a partial-method body that internally
/// calls <c>ILogger.Log</c> directly — without this guard, SK0020 would fire against the
/// generated implementation of every correctly-authored <c>[LoggerMessage]</c> method
/// platform-wide, which would be a self-defeating false positive at the core of this rule's
/// entire purpose.
/// </para>
/// <para>
/// <strong>Suppression:</strong> both diagnostics carry a <c>SharedKernel.Testing</c> namespace
/// exemption (the established SK0001/SK0007/SK0013 <see cref="SyntaxNode.Parent"/> walk pattern)
/// — the in-memory <c>ILogger</c>/<c>ILoggerFactory</c> test double (P-258, 16.Testing)
/// legitimately implements/exercises the <c>ILogger</c> surface directly as its own subject
/// under test. Per-call-site suppression via <c>#pragma warning disable SK0020</c> /
/// <c>SK0021</c> is otherwise permitted; document the rationale inline.
/// </para>
/// <para>Introduced in WO-041 P-250.</para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class LoggingAuthoringStyleAnalyzer : AnalyzerBase
{
    private const string Sk0020Id = "SK0020";
    private const string Sk0021Id = "SK0021";

    /// <summary>The namespace prefix exempted from both SK0020 and SK0021.</summary>
    private const string ExemptedNamespacePrefix = "SharedKernel.Testing";

    private const string LoggerExtensionsTypeName = "Microsoft.Extensions.Logging.LoggerExtensions";
    private const string ILoggerTypeName = "Microsoft.Extensions.Logging.ILogger";

    /// <summary>The diagnostic descriptor for SK0020.</summary>
    public static readonly DiagnosticDescriptor DirectILoggerExtensionMethodUsageRule = CreateDescriptor(
        id: Sk0020Id,
        title: "Direct ILogger extension-method usage",
        messageFormat: "Call to '{0}' resolves directly to Microsoft.Extensions.Logging.{1}. " +
                       "Author this log statement via the [LoggerMessage] source-generated " +
                       "partial-method pattern with an explicit EventId inside the calling " +
                       "assembly's domain-reserved range instead of a direct ILogger call.",
        category: Design,
        defaultSeverity: DiagnosticSeverity.Warning,
        readmeAnchor: "sk0020-directiloggerextensionmethodusage"
    );

    /// <summary>The diagnostic descriptor for SK0021.</summary>
    public static readonly DiagnosticDescriptor HandWrittenLoggerMessageDefineDelegateRule = CreateDescriptor(
        id: Sk0021Id,
        title: "Hand-written LoggerMessage.Define delegate",
        messageFormat: "Hand-written call to 'LoggerMessage.{0}' bypasses the [LoggerMessage] " +
                       "source generator. Replace the static delegate field and Define call with " +
                       "a [LoggerMessage]-attributed static partial method.",
        category: Design,
        defaultSeverity: DiagnosticSeverity.Warning,
        readmeAnchor: "sk0021-handwrittenloggermessagedefinedelegate"
    );

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DirectILoggerExtensionMethodUsageRule, HandWrittenLoggerMessageDefineDelegateRule);

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

        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            return;

        if (IsInsideExemptedNamespace(invocation))
            return;

        // SK0021 — syntax-only qualified "LoggerMessage.Define*" shape.
        if (TryGetSimpleName(memberAccess.Expression) == "LoggerMessage"
            && memberAccess.Name.Identifier.Text.StartsWith("Define", System.StringComparison.Ordinal))
        {
            context.ReportDiagnostic(
                Diagnostic.Create(
                    HandWrittenLoggerMessageDefineDelegateRule,
                    memberAccess.Name.Identifier.GetLocation(),
                    memberAccess.Name.Identifier.Text));
            return;
        }

        // SK0020 — cheap syntactic pre-filter before paying for symbol resolution: every
        // LoggerExtensions method name starts with "Log", and ILogger.Log is exactly "Log".
        var invokedName = memberAccess.Name.Identifier.Text;
        if (!invokedName.StartsWith("Log", System.StringComparison.Ordinal))
            return;

        var symbolInfo = context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken);
        if (symbolInfo.Symbol is not IMethodSymbol methodSymbol)
            return;

        var containingType = methodSymbol.ContainingType;
        if (containingType is null)
            return;

        var containingTypeFullName = GetFullTypeName(containingType);

        var isLoggerExtensionsCall =
            containingTypeFullName == LoggerExtensionsTypeName
            && invokedName.StartsWith("Log", System.StringComparison.Ordinal);

        var isILoggerLogCall =
            containingTypeFullName == ILoggerTypeName
            && invokedName == "Log";

        if (!isLoggerExtensionsCall && !isILoggerLogCall)
            return;

        context.ReportDiagnostic(
            Diagnostic.Create(
                DirectILoggerExtensionMethodUsageRule,
                memberAccess.Name.Identifier.GetLocation(),
                invokedName,
                containingType.Name));
    }

    private static string? TryGetSimpleName(ExpressionSyntax expression) =>
        expression switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.Text,
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.Text,
            QualifiedNameSyntax qualified => qualified.Right.Identifier.Text,
            _ => null,
        };

    private static string GetFullTypeName(INamedTypeSymbol type) =>
        type.ContainingNamespace is { IsGlobalNamespace: false }
            ? $"{type.ContainingNamespace.ToDisplayString()}.{type.Name}"
            : type.Name;

    /// <summary>
    /// Walks ancestor syntax nodes looking for a namespace declaration whose qualified name
    /// starts with <see cref="ExemptedNamespacePrefix"/>. Same pattern as SK0001/SK0007/SK0013.
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

            if (nsName is not null &&
                nsName.StartsWith(ExemptedNamespacePrefix, System.StringComparison.Ordinal))
            {
                return true;
            }

            current = current.Parent;
        }

        return false;
    }
}

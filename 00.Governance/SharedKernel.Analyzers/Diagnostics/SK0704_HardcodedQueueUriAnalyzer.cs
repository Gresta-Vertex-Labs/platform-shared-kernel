using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SharedKernel.Analyzers.Diagnostics;

/// <summary>
/// SK0704 — Fires when <c>GetSendEndpoint</c> is called with a <c>new Uri("queue:...")</c> or
/// <c>new Uri("exchange:...")</c> literal argument.
/// </summary>
/// <remarks>
/// <para>
/// Hardcoded queue or exchange URI strings in <c>GetSendEndpoint</c> calls tie the producer to
/// a specific broker topology. When the queue name, exchange name, or transport changes (e.g.,
/// RabbitMQ → Azure Service Bus, or a queue rename during a rolling deployment), every call
/// site must be updated manually. Convention-based endpoint resolution via
/// <c>IEndpointNameFormatter.GetDestinationAddress&lt;TMessage&gt;()</c> centralises this
/// concern and survives broker configuration changes automatically.
/// </para>
/// <para>
/// This is a <strong>syntax-only</strong> check — no <see cref="SemanticModel"/> is required.
/// The method name <c>GetSendEndpoint</c> and the URI schemes <c>"queue:"</c> /
/// <c>"exchange:"</c> are unique enough in practice; the string literal scheme check is
/// case-insensitive to handle both <c>"queue:order-commands"</c> and <c>"Queue:order-commands"</c>.
/// </para>
/// <para>
/// <b>Covered forms:</b>
/// <list type="bullet">
///   <item><c>provider.GetSendEndpoint(new Uri("queue:order-commands"))</c></item>
///   <item><c>provider.GetSendEndpoint(new Uri("exchange:order-events"))</c></item>
/// </list>
/// </para>
/// <para>
/// <b>Pass-through forms (not flagged):</b>
/// <list type="bullet">
///   <item><c>provider.GetSendEndpoint(formatter.GetDestinationAddress&lt;OrderCommand&gt;())</c> — not a literal Uri</item>
///   <item><c>provider.GetSendEndpoint(new Uri(someVariable))</c> — non-literal first argument</item>
///   <item><c>provider.GetSendEndpoint(new Uri("https://..."))</c> — scheme does not start with queue/exchange</item>
/// </list>
/// </para>
/// <para>
/// <b>Suppression:</b> use <c>#pragma warning disable SK0704</c> at the call site only when a
/// fixed, environment-invariant queue address is genuinely required (e.g., a dead-letter queue
/// URI in an isolated test fixture). Document the rationale inline.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class HardcodedQueueUriAnalyzer : AnalyzerBase
{
    private const string DiagnosticId = "SK0704";
    private const string GetSendEndpointName = "GetSendEndpoint";
    private const string UriTypeName = "Uri";

    /// <summary>The diagnostic descriptor for SK0704.</summary>
    public static readonly DiagnosticDescriptor Rule = CreateDescriptor(
        id: DiagnosticId,
        title: "Hardcoded queue or exchange URI in GetSendEndpoint",
        messageFormat: "Do not pass a hardcoded queue or exchange URI string to GetSendEndpoint. Use convention-based endpoint resolution via IEndpointNameFormatter.",
        category: Usage,
        defaultSeverity: DiagnosticSeverity.Warning,
        readmeAnchor: "sk0704-hardcodedqueueuriingetsendendpoint"
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
            AnalyzeInvocation,
            SyntaxKind.InvocationExpression
        );
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        // Must be a call to GetSendEndpoint
        if (!IsGetSendEndpointCall(invocation))
            return;

        // Inspect each argument for a new Uri("queue:...") or new Uri("exchange:...") pattern
        foreach (var argument in invocation.ArgumentList.Arguments)
        {
            if (TryGetViolatingUriCreation(argument.Expression, out var uriCreation))
            {
                context.ReportDiagnostic(
                    Diagnostic.Create(Rule, uriCreation!.GetLocation())
                );
                return; // Report once per GetSendEndpoint call
            }
        }
    }

    private static bool IsGetSendEndpointCall(InvocationExpressionSyntax invocation)
    {
        var methodName = invocation.Expression switch
        {
            IdentifierNameSyntax id => id.Identifier.Text,
            MemberAccessExpressionSyntax { Name: IdentifierNameSyntax nameId } => nameId.Identifier.Text,
            _ => null,
        };

        return methodName == GetSendEndpointName;
    }

    private static bool TryGetViolatingUriCreation(
        ExpressionSyntax expression,
        out ExpressionSyntax? violatingNode)
    {
        violatingNode = null;

        switch (expression)
        {
            // new Uri("queue:...")
            case ObjectCreationExpressionSyntax objectCreation:
                if (!IsUriTypeName(objectCreation.Type))
                    return false;

                if (!HasForbiddenLiteralFirstArgument(objectCreation.ArgumentList))
                    return false;

                violatingNode = objectCreation;
                return true;

            // new("queue:...") — implicit new where context resolves to Uri
            // We detect by checking if it's inside a GetSendEndpoint call with a forbidden string
            case ImplicitObjectCreationExpressionSyntax implicitCreation:
                if (!HasForbiddenLiteralFirstArgument(implicitCreation.ArgumentList))
                    return false;

                violatingNode = implicitCreation;
                return true;

            default:
                return false;
        }
    }

    private static bool IsUriTypeName(TypeSyntax typeSyntax)
    {
        var name = typeSyntax switch
        {
            IdentifierNameSyntax id => id.Identifier.Text,
            QualifiedNameSyntax qualified when qualified.Right is IdentifierNameSyntax rightId
                => rightId.Identifier.Text,
            _ => null,
        };

        return name == UriTypeName;
    }

    private static bool HasForbiddenLiteralFirstArgument(ArgumentListSyntax? argumentList)
    {
        if (argumentList is null || argumentList.Arguments.Count == 0)
            return false;

        var firstArg = argumentList.Arguments[0].Expression;

        if (firstArg is not LiteralExpressionSyntax literal ||
            !literal.IsKind(SyntaxKind.StringLiteralExpression))
        {
            return false;
        }

        var value = literal.Token.ValueText;
        return value.StartsWith("queue:", StringComparison.OrdinalIgnoreCase) ||
               value.StartsWith("exchange:", StringComparison.OrdinalIgnoreCase);
    }
}

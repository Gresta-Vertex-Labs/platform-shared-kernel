using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SharedKernel.Analyzers.Diagnostics;

/// <summary>
/// SK0703 — Fires when <c>AddSingleton</c> is called with a type argument whose simple name is
/// exactly <c>"IMessageBus"</c> or <c>"IEventPublisher"</c>.
/// </summary>
/// <remarks>
/// <para>
/// MassTransit's consume pipeline creates a new scope for each consumed message. Registering
/// <c>IMessageBus</c> or <c>IEventPublisher</c> as a singleton causes scope pollution and race
/// conditions under concurrent load — the singleton is shared across all consume scopes and
/// loses the per-scope state (outbox, correlation IDs, etc.).
/// </para>
/// <para>
/// This is a <strong>syntax-only</strong> check — no <see cref="SemanticModel"/> is required.
/// The match is on the <em>exact</em> simple names <c>"IMessageBus"</c> and <c>"IEventPublisher"</c>,
/// never a prefix: sibling contracts that share the prefix are legitimately singletons.
/// The motivating case was the former <c>IMessageBusProbe</c> (replaced by an <c>IReadinessProbe</c> in
/// WO-086): <c>MessagingBusBuilder</c> registered it as a singleton, and a prefix match flagged a consumer
/// doing the same.
/// </para>
/// <para>
/// <b>Covered forms:</b>
/// <list type="bullet">
///   <item><c>services.AddSingleton&lt;IMessageBus, MassTransitMessageBus&gt;()</c></item>
///   <item><c>services.AddSingleton&lt;IEventPublisher, MassTransitEventPublisher&gt;()</c></item>
///   <item><c>services.AddSingleton&lt;IMessageBus&gt;(factory)</c></item>
/// </list>
/// </para>
/// <para>
/// <b>Suppression:</b> use <c>#pragma warning disable SK0703</c> at the call site only when the
/// DI container semantics are provably equivalent to scoped behaviour. Document the reason inline.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MessageBusSingletonRegistrationAnalyzer : AnalyzerBase
{
    private const string DiagnosticId = "SK0703";
    private const string AddSingletonName = "AddSingleton";

    /// <summary>The diagnostic descriptor for SK0703.</summary>
    public static readonly DiagnosticDescriptor Rule = CreateDescriptor(
        id: DiagnosticId,
        title: "IMessageBus or IEventPublisher registered as Singleton",
        messageFormat: "IMessageBus and IEventPublisher must be registered as Scoped, not Singleton. Use AddScoped<{0}, ...>() instead.",
        category: Usage,
        defaultSeverity: DiagnosticSeverity.Warning,
        readmeAnchor: "sk0703-messagebussingletonregistration"
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

        // Must be a call to AddSingleton
        if (!IsAddSingletonCall(invocation))
            return;

        // Must be a generic invocation: AddSingleton<T, ...>() or AddSingleton<T>(...)
        var genericName = GetGenericName(invocation.Expression);
        if (genericName is null)
            return;

        // Check every type argument — SK0703 fires if any is exactly one of the scoped contracts
        foreach (var typeArg in genericName.TypeArgumentList.Arguments)
        {
            var simpleName = ExtractSimpleName(typeArg);
            if (simpleName is null)
                continue;

            if (string.Equals(simpleName, "IMessageBus", StringComparison.Ordinal) ||
                string.Equals(simpleName, "IEventPublisher", StringComparison.Ordinal))
            {
                context.ReportDiagnostic(
                    Diagnostic.Create(Rule, invocation.GetLocation(), simpleName)
                );
                return; // Report once per invocation
            }
        }
    }

    private static bool IsAddSingletonCall(InvocationExpressionSyntax invocation)
    {
        var name = invocation.Expression switch
        {
            IdentifierNameSyntax id => id.Identifier.Text,
            GenericNameSyntax generic => generic.Identifier.Text,
            MemberAccessExpressionSyntax memberAccess when memberAccess.Name is IdentifierNameSyntax id2
                => id2.Identifier.Text,
            MemberAccessExpressionSyntax memberAccess when memberAccess.Name is GenericNameSyntax gn
                => gn.Identifier.Text,
            _ => null,
        };

        return name == AddSingletonName;
    }

    private static GenericNameSyntax? GetGenericName(ExpressionSyntax expression) =>
        expression switch
        {
            GenericNameSyntax generic => generic,
            MemberAccessExpressionSyntax { Name: GenericNameSyntax gn } => gn,
            _ => null,
        };

    private static string? ExtractSimpleName(TypeSyntax typeSyntax) =>
        typeSyntax switch
        {
            IdentifierNameSyntax id => id.Identifier.Text,
            GenericNameSyntax generic => generic.Identifier.Text,
            QualifiedNameSyntax qualified when qualified.Right is IdentifierNameSyntax rightId
                => rightId.Identifier.Text,
            QualifiedNameSyntax qualified when qualified.Right is GenericNameSyntax rightGn
                => rightGn.Identifier.Text,
            _ => null,
        };
}

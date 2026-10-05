using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SharedKernel.Analyzers.Diagnostics;

/// <summary>
/// SK0705 — Fires when <c>AddScoped</c> or <c>AddSingleton</c> is called with a type argument
/// that is a generic <c>IFaultConsumer&lt;TMessage&gt;</c> reference.
/// </summary>
/// <remarks>
/// <para>
/// Fault consumers must be wired through <c>MessagingBusBuilder.AddFaultConsumer&lt;TMessage,
/// TConsumer&gt;()</c>, which registers the MassTransit <c>Fault&lt;T&gt;</c> adapter that
/// translates a raw MassTransit fault context into the platform <c>IFaultConsumer&lt;T&gt;</c>
/// abstraction. Direct DI registration via <c>AddScoped</c> or <c>AddSingleton</c> bypasses this
/// adapter chain — the consumer never receives a translated <c>IFaultConsumer&lt;T&gt;</c>
/// invocation because no MassTransit consumer subscribes to <c>Fault&lt;T&gt;</c> on its behalf.
/// </para>
/// <para>
/// This is a <strong>syntax-only</strong> check — no <see cref="SemanticModel"/> is required.
/// The check inspects each type argument of the registration call for a
/// <see cref="GenericNameSyntax"/> whose <see cref="SyntaxToken.Text"/> on
/// <see cref="GenericNameSyntax.Identifier"/> is exactly <c>"IFaultConsumer"</c>.
/// </para>
/// <para>
/// <b>Covered forms:</b>
/// <list type="bullet">
///   <item><c>services.AddScoped&lt;IFaultConsumer&lt;OrderPlaced&gt;, OrderFaultConsumer&gt;()</c></item>
///   <item><c>services.AddSingleton&lt;IFaultConsumer&lt;OrderPlaced&gt;&gt;()</c></item>
/// </list>
/// </para>
/// <para>
/// <b>Suppression:</b> use <c>#pragma warning disable SK0705</c> at the call site only when
/// explicitly bypassing the builder is intentional. Document the rationale inline.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class FaultConsumerDirectRegistrationAnalyzer : AnalyzerBase
{
    private const string DiagnosticId = "SK0705";
    private const string AddScopedName = "AddScoped";
    private const string AddSingletonName = "AddSingleton";
    private const string FaultConsumerInterfaceName = "IFaultConsumer";

    /// <summary>The diagnostic descriptor for SK0705.</summary>
    public static readonly DiagnosticDescriptor Rule = CreateDescriptor(
        id: DiagnosticId,
        title: "IFaultConsumer registered directly via AddScoped or AddSingleton",
        messageFormat: "IFaultConsumer<TMessage> must not be registered directly via {0}. Use MessagingBusBuilder.AddFaultConsumer<TMessage, TConsumer>() to wire the Fault<T> adapter chain.",
        category: Usage,
        defaultSeverity: DiagnosticSeverity.Warning,
        readmeAnchor: "sk0705-faultconsumerdirectregistration"
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

        var methodName = GetInvokedMethodName(invocation);
        if (methodName != AddScopedName && methodName != AddSingletonName)
            return;

        var genericName = GetGenericName(invocation.Expression);
        if (genericName is null)
            return;

        foreach (var typeArg in genericName.TypeArgumentList.Arguments)
        {
            if (typeArg is GenericNameSyntax { Identifier.Text: FaultConsumerInterfaceName })
            {
                context.ReportDiagnostic(
                    Diagnostic.Create(Rule, invocation.GetLocation(), methodName)
                );
                return; // Report once per invocation
            }
        }
    }

    private static string? GetInvokedMethodName(InvocationExpressionSyntax invocation) =>
        invocation.Expression switch
        {
            IdentifierNameSyntax id => id.Identifier.Text,
            GenericNameSyntax generic => generic.Identifier.Text,
            MemberAccessExpressionSyntax memberAccess when memberAccess.Name is IdentifierNameSyntax id2
                => id2.Identifier.Text,
            MemberAccessExpressionSyntax memberAccess when memberAccess.Name is GenericNameSyntax gn
                => gn.Identifier.Text,
            _ => null,
        };

    private static GenericNameSyntax? GetGenericName(ExpressionSyntax expression) =>
        expression switch
        {
            GenericNameSyntax generic => generic,
            MemberAccessExpressionSyntax { Name: GenericNameSyntax gn } => gn,
            _ => null,
        };
}

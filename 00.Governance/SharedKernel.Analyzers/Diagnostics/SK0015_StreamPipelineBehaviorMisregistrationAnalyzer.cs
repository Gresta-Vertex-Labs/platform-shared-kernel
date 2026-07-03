using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SharedKernel.Analyzers.Diagnostics;

/// <summary>
/// SK0015 — Fires when a type-based DI registration call (<c>AddTransient</c>, <c>AddScoped</c>,
/// or <c>AddSingleton</c>) registers a service type resolving to the open generic
/// <c>MediatR.IPipelineBehavior&lt;,&gt;</c> against an implementation type that itself implements
/// <c>MediatR.IStreamPipelineBehavior&lt;,&gt;</c>, outside a method named
/// <c>AddStreamingBehaviors</c>.
/// </summary>
/// <remarks>
/// <para>
/// MediatR dispatches <c>IStreamRequest&lt;TResponse&gt;</c> through
/// <c>IStreamPipelineBehavior&lt;,&gt;</c>, never through <c>IPipelineBehavior&lt;,&gt;</c>. A
/// streaming behavior registered against the wrong interface is silently never invoked — no
/// exception, no warning, the behavior simply never runs.
/// </para>
/// <para>
/// <strong>Semantic-model requirement.</strong> Determining whether the implementation type
/// implements <c>IStreamPipelineBehavior&lt;,&gt;</c> requires resolving its interface list via
/// <see cref="SemanticModel.GetTypeInfo(SyntaxNode, System.Threading.CancellationToken)"/> — a
/// naming-heuristic approach (mirroring SK0708's <c>"BatchConsumer"</c> substring convention) was
/// deliberately rejected because the five known streaming behavior names are a convention, not a
/// structural guarantee, and a semantic check avoids the false-negative risk of a future streaming
/// behavior not following a <c>Stream*</c> naming prefix. SK0015 is the second SK rule in this
/// domain (after SK0011) to require semantic model resolution.
/// </para>
/// <para>
/// <strong>Self-exemption.</strong> The <c>AddStreamingBehaviors</c> method-name check is
/// syntax-only and short-circuits before the semantic-model call — the canonical builder method
/// is the single sanctioned call site for streaming-behavior registration.
/// </para>
/// <para>
/// <b>Covered form:</b>
/// <c>services.AddTransient(typeof(IPipelineBehavior&lt;,&gt;), typeof(StreamMetricsBehavior&lt;,&gt;))</c>
/// called outside <c>AddStreamingBehaviors</c>.
/// </para>
/// <para>
/// <b>Suppression:</b> use <c>#pragma warning disable SK0015</c> at the call site only for a
/// deliberate hybrid unary/streaming behavior type; document why the type intentionally implements
/// both interfaces.
/// </para>
/// <para>Introduced WO-038 P-235.</para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class StreamPipelineBehaviorMisregistrationAnalyzer : AnalyzerBase
{
    private const string DiagnosticId = "SK0015";
    private const string SelfExemptMethodName = "AddStreamingBehaviors";
    private const string PipelineBehaviorSimpleName = "IPipelineBehavior";
    private const string StreamPipelineBehaviorSimpleName = "IStreamPipelineBehavior";
    private const string MediatRNamespace = "MediatR";

    private static readonly ImmutableHashSet<string> RegistrationMethodNames =
        ImmutableHashSet.Create(StringComparer.Ordinal, "AddTransient", "AddScoped", "AddSingleton");

    /// <summary>The diagnostic descriptor for SK0015.</summary>
    public static readonly DiagnosticDescriptor Rule = CreateDescriptor(
        id: DiagnosticId,
        title: "Streaming pipeline behavior registered against IPipelineBehavior<,>",
        messageFormat: "'{0}' implements IStreamPipelineBehavior<,> but is registered against " +
                       "IPipelineBehavior<,>. MediatR dispatches streaming requests through " +
                       "IStreamPipelineBehavior<,> only — this registration is silently never invoked. " +
                       "Call ApplicationBehaviorsBuilder.AddStreamingBehaviors() instead.",
        category: Usage,
        defaultSeverity: DiagnosticSeverity.Warning,
        readmeAnchor: "sk0015-streampipelinebehaviormisregistration"
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
        if (methodName is null || !RegistrationMethodNames.Contains(methodName))
            return;

        // Self-exemption: syntax-only, short-circuits before any semantic model call.
        if (IsInsideAddStreamingBehaviorsMethod(invocation))
            return;

        var arguments = invocation.ArgumentList.Arguments;
        if (arguments.Count != 2)
            return;

        if (arguments[0].Expression is not TypeOfExpressionSyntax serviceTypeOf ||
            arguments[1].Expression is not TypeOfExpressionSyntax implementationTypeOf)
        {
            return;
        }

        var serviceType = context.SemanticModel
            .GetTypeInfo(serviceTypeOf.Type, context.CancellationToken)
            .Type;

        if (serviceType is not INamedTypeSymbol { Arity: 2 } serviceNamed ||
            !IsMediatRInterface(serviceNamed, PipelineBehaviorSimpleName))
        {
            return;
        }

        var implementationType = context.SemanticModel
            .GetTypeInfo(implementationTypeOf.Type, context.CancellationToken)
            .Type;

        if (implementationType is not INamedTypeSymbol implementationNamed)
            return;

        if (!ImplementsMediatRStreamPipelineBehavior(implementationNamed))
            return;

        context.ReportDiagnostic(
            Diagnostic.Create(Rule, invocation.GetLocation(), implementationNamed.Name)
        );
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

    /// <summary>
    /// Walks up to the nearest enclosing <see cref="MethodDeclarationSyntax"/> and returns
    /// <see langword="true"/> when its identifier is exactly <see cref="SelfExemptMethodName"/>.
    /// </summary>
    private static bool IsInsideAddStreamingBehaviorsMethod(SyntaxNode node)
    {
        var current = node.Parent;
        while (current is not null)
        {
            if (current is MethodDeclarationSyntax methodDecl)
                return methodDecl.Identifier.Text == SelfExemptMethodName;

            current = current.Parent;
        }

        return false;
    }

    private static bool IsMediatRInterface(INamedTypeSymbol symbol, string simpleName)
    {
        var original = symbol.OriginalDefinition;
        return original.Name == simpleName
            && original.ContainingNamespace is { IsGlobalNamespace: false } ns
            && ns.Name == MediatRNamespace;
    }

    private static bool ImplementsMediatRStreamPipelineBehavior(INamedTypeSymbol type)
    {
        // AllInterfaces returns an empty set for an unbound generic type symbol (the shape
        // produced by typeof(StreamFixtureBehavior<,>)) — OriginalDefinition resolves to the
        // bound generic type definition, whose AllInterfaces is populated correctly.
        foreach (var iface in type.OriginalDefinition.AllInterfaces)
        {
            if (iface.Arity == 2 && IsMediatRInterface(iface, StreamPipelineBehaviorSimpleName))
                return true;
        }

        return false;
    }
}

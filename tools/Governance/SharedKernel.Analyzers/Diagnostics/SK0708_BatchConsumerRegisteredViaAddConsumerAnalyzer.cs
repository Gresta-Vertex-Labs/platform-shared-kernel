using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SharedKernel.Analyzers.Diagnostics;

/// <summary>
/// SK0708 — Fires when <c>AddConsumer</c> is called with a single type argument whose
/// identifier text contains <c>"BatchConsumer"</c> as a substring.
/// </summary>
/// <remarks>
/// <para>
/// <c>AddConsumer&lt;T&gt;()</c> registers a consumer that processes messages one at a time and
/// ignores any <c>MessageLimit</c> / <c>TimeLimit</c> batch configuration. Batch consumers must
/// be registered via <c>MessagingBusBuilder.AddBatchConsumer&lt;T&gt;()</c> so the platform
/// applies the configured batch window.
/// </para>
/// <para>
/// This is a <strong>naming-convention-guided heuristic</strong>: it fires only when the type
/// argument's identifier text contains <c>"BatchConsumer"</c> as a substring (case-sensitive).
/// Batch consumer implementation classes <em>must</em> contain <c>"BatchConsumer"</c> in their
/// class name (e.g., <c>OrderBatchConsumer</c>) for this rule to provide coverage. A class named
/// <c>OrderProcessor</c> that is, in fact, a batch consumer will not be detected — this is a
/// documented false-negative limitation, not a bug.
/// </para>
/// <para>
/// This is a <strong>syntax-only</strong> check — no <see cref="SemanticModel"/> is required.
/// </para>
/// <para>
/// <b>Covered forms:</b>
/// <list type="bullet">
///   <item><c>builder.AddConsumer&lt;OrderBatchConsumer&gt;()</c></item>
/// </list>
/// </para>
/// <para>
/// <b>Pass-through forms (not flagged):</b>
/// <list type="bullet">
///   <item><c>builder.AddBatchConsumer&lt;OrderBatchConsumer&gt;()</c> — correct registration method</item>
///   <item><c>builder.AddConsumer&lt;OrderCommandConsumer&gt;()</c> — type name does not contain "BatchConsumer"</item>
/// </list>
/// </para>
/// <para>
/// <b>Suppression:</b> use <c>#pragma warning disable SK0708</c> at the call site when a batch
/// consumer class genuinely must be registered individually (e.g., a test fixture that processes
/// one message at a time by design). There is no suppression namespace — SK0708 fires globally.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class BatchConsumerRegisteredViaAddConsumerAnalyzer : AnalyzerBase
{
    private const string DiagnosticId = "SK0708";
    private const string AddConsumerName = "AddConsumer";
    private const string BatchConsumerSubstring = "BatchConsumer";

    /// <summary>The diagnostic descriptor for SK0708.</summary>
    public static readonly DiagnosticDescriptor Rule = CreateDescriptor(
        id: DiagnosticId,
        title: "Batch consumer registered via AddConsumer instead of AddBatchConsumer",
        messageFormat: "'{0}' appears to be a batch consumer (name contains 'BatchConsumer') but is registered via AddConsumer<T>(). Use MessagingBusBuilder.AddBatchConsumer<T>() to apply MessageLimit and TimeLimit batch configuration.",
        category: Usage,
        defaultSeverity: DiagnosticSeverity.Warning,
        readmeAnchor: "sk0708-batchconsumerregisteredviaaddconsumer"
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
        if (methodName != AddConsumerName)
            return;

        var genericName = GetGenericName(invocation.Expression);
        if (genericName is null)
            return;

        // Only the single-type-argument form is in scope
        if (genericName.TypeArgumentList.Arguments.Count != 1)
            return;

        var typeArg = genericName.TypeArgumentList.Arguments[0];
        var simpleName = ExtractSimpleName(typeArg);
        if (simpleName is null)
            return;

        if (simpleName.Contains(BatchConsumerSubstring, StringComparison.Ordinal))
        {
            context.ReportDiagnostic(
                Diagnostic.Create(Rule, invocation.GetLocation(), simpleName)
            );
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

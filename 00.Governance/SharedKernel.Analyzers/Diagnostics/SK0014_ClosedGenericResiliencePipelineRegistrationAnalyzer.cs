using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SharedKernel.Analyzers.Diagnostics;

/// <summary>
/// SK0014 — Fires when the arity-1 generic form <c>ResiliencePipeline&lt;T&gt;</c> is used
/// anywhere in production code — as a DI registration type argument, a constructor or method
/// parameter type, a field type, or a local variable type.
/// </summary>
/// <remarks>
/// <para>
/// Polly v8 resilience pipelines are registered and resolved via the non-generic
/// <c>Polly.ResiliencePipeline</c> type, keyed by a string policy name
/// (<c>ResiliencePipelineProvider&lt;string&gt;</c> / <c>AddResiliencePipeline("policy-name", ...)</c>).
/// A closed-generic <c>ResiliencePipeline&lt;TResponse&gt;</c> registration silently falls back to a
/// no-op pipeline whenever the resolved key does not exactly match the closed type used at the
/// call site, defeating retry/circuit-breaker protection with no runtime warning.
/// </para>
/// <para>
/// This is a <strong>syntax-only</strong> check — no <see cref="SemanticModel"/> is required. The
/// arity-1 generic form <c>ResiliencePipeline&lt;T&gt;</c> is textually distinguishable from the
/// correct arity-0 <c>ResiliencePipeline</c> simple name (Polly v8's non-generic type).
/// </para>
/// <para>
/// <b>Covered forms:</b>
/// <list type="bullet">
///   <item><c>services.AddSingleton&lt;ResiliencePipeline&lt;TResponse&gt;&gt;(...)</c></item>
///   <item><c>private readonly ResiliencePipeline&lt;HttpResponseMessage&gt; _pipeline;</c></item>
///   <item><c>void Configure(ResiliencePipeline&lt;string&gt; pipeline)</c></item>
/// </list>
/// </para>
/// <para>
/// <b>Suppression:</b> use <c>#pragma warning disable SK0014</c> at the call site only when a
/// third-party library API genuinely requires the closed-generic Polly type; document the
/// rationale inline. There is no suppression namespace — SK0014 fires globally.
/// </para>
/// <para>Introduced WO-038 P-235.</para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ClosedGenericResiliencePipelineRegistrationAnalyzer : AnalyzerBase
{
    private const string DiagnosticId = "SK0014";
    private const string ResiliencePipelineTypeName = "ResiliencePipeline";

    /// <summary>The diagnostic descriptor for SK0014.</summary>
    public static readonly DiagnosticDescriptor Rule = CreateDescriptor(
        id: DiagnosticId,
        title: "Closed-generic ResiliencePipeline<T> registration",
        messageFormat: "ResiliencePipeline<{0}> uses the arity-1 generic form. Register and resolve Polly v8 " +
                       "resilience pipelines via the non-generic, string-keyed Polly.ResiliencePipeline type " +
                       "instead — a closed-generic registration silently falls back to a no-op pipeline when " +
                       "the resolved key does not exactly match the closed type used at the call site.",
        category: Usage,
        defaultSeverity: DiagnosticSeverity.Warning,
        readmeAnchor: "sk0014-closedgenericresiliencepipelineregistration"
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
            AnalyzeGenericName,
            SyntaxKind.GenericName
        );
    }

    private static void AnalyzeGenericName(SyntaxNodeAnalysisContext context)
    {
        var genericName = (GenericNameSyntax)context.Node;

        if (genericName.Identifier.Text != ResiliencePipelineTypeName)
            return;

        if (genericName.TypeArgumentList.Arguments.Count != 1)
            return;

        var typeArgumentText = genericName.TypeArgumentList.Arguments[0].ToString();

        context.ReportDiagnostic(
            Diagnostic.Create(Rule, genericName.GetLocation(), typeArgumentText)
        );
    }
}

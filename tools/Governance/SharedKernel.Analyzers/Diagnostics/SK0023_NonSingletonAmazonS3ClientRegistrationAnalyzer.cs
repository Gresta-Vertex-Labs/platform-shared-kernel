using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SharedKernel.Analyzers.Diagnostics;

/// <summary>
/// SK0023 — Fires when <c>AddScoped</c> or <c>AddTransient</c> is called with a first type
/// argument whose simple name is exactly <c>"IAmazonS3"</c>.
/// </summary>
/// <remarks>
/// <para>
/// <c>Amazon.S3.IAmazonS3</c> is thread-safe and connection/credential-pooled internally per the
/// AWS SDK's own documented contract and <c>src/Infrastructure/Storage/CLAUDE.md</c>'s explicit "Provider
/// <c>IAmazonS3</c> clients are singletons" implementation rule. Registering it as scoped or
/// transient constructs a new client (and thus a new connection pool) per resolution, which is
/// expensive under load and can exhaust ephemeral ports.
/// </para>
/// <para>
/// SK0023 is the structural inverse of SK0703 (<see cref="MessageBusSingletonRegistrationAnalyzer"/>,
/// which flags <c>AddSingleton&lt;IMessageBus&gt;</c> because that type must be scoped): SK0023
/// flags <c>AddScoped</c>/<c>AddTransient</c> because <c>IAmazonS3</c> must always be singleton.
/// Type-argument extraction reuses SK0703's exact technique — <see cref="GenericNameSyntax.TypeArgumentList"/>'s
/// first argument as an <see cref="IdentifierNameSyntax"/>, simple-name exact match against
/// <c>"IAmazonS3"</c> — covering both the one-argument factory form
/// <c>AddScoped&lt;IAmazonS3&gt;(sp =&gt; ...)</c> and the two-argument form
/// <c>AddTransient&lt;IAmazonS3, AmazonS3Client&gt;()</c>. This is a <strong>syntax-only</strong>
/// check — no <see cref="SemanticModel"/> is required.
/// </para>
/// <para>
/// <strong>No suppression namespace.</strong> SK0023 fires globally, mirroring SK0703's/SK0014's
/// "fires globally" convention — <c>IAmazonS3</c> must be a singleton wherever it is registered,
/// not only inside <c>SharedKernel.Storage.S3</c>/<c>SharedKernel.Storage.Obs</c>. Those packages no
/// longer register <c>IAmazonS3</c> in DI at all — <c>AddSharedKernelStorage().AddS3(...)</c>/<c>.AddObs(...)</c>
/// keep one client per connection inside a keyed singleton — so today the rule guards a consuming
/// microservice that registers its own <c>IAmazonS3</c> with the wrong lifetime.
/// </para>
/// <para>
/// <strong>Suppression:</strong> use <c>#pragma warning disable SK0023</c> at the call site only
/// when a test fixture or a genuinely short-lived client is required. Document the reason inline.
/// </para>
/// <para>Introduced in WO-043 P-271.</para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NonSingletonAmazonS3ClientRegistrationAnalyzer : AnalyzerBase
{
    private const string DiagnosticId = "SK0023";
    private const string AddScopedName = "AddScoped";
    private const string AddTransientName = "AddTransient";
    private const string TargetSimpleName = "IAmazonS3";

    /// <summary>The diagnostic descriptor for SK0023.</summary>
    public static readonly DiagnosticDescriptor Rule = CreateDescriptor(
        id: DiagnosticId,
        title: "IAmazonS3 registered as Scoped or Transient",
        messageFormat: "IAmazonS3 must be registered as Singleton, not {0}. Use AddSingleton<{1}, ...>() instead.",
        category: Usage,
        defaultSeverity: DiagnosticSeverity.Warning,
        readmeAnchor: "sk0023-nonsingletonamazons3clientregistration"
    );

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

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

        var registrationMethodName = GetRegistrationMethodName(invocation);
        if (registrationMethodName is null)
            return;

        // Must be a generic invocation: AddScoped<T>(...) / AddTransient<T, TImpl>(...)
        var genericName = GetGenericName(invocation.Expression);
        if (genericName is null || genericName.TypeArgumentList.Arguments.Count == 0)
            return;

        // Only the FIRST type argument is checked — this is always the DI service type,
        // covering both AddScoped<IAmazonS3>(factory) and AddTransient<IAmazonS3, AmazonS3Client>().
        var firstTypeArgument = genericName.TypeArgumentList.Arguments[0];
        var simpleName = ExtractSimpleName(firstTypeArgument);

        if (simpleName == TargetSimpleName)
        {
            context.ReportDiagnostic(
                Diagnostic.Create(Rule, invocation.GetLocation(), registrationMethodName, simpleName)
            );
        }
    }

    private static string? GetRegistrationMethodName(InvocationExpressionSyntax invocation)
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

        return name is AddScopedName or AddTransientName ? name : null;
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

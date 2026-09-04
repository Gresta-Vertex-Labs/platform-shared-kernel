using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SharedKernel.Analyzers.Diagnostics;

/// <summary>
/// SK0033 — Fires on any of three AutoMapper reflection-based mapping shapes: (1) a class
/// declaration whose base type resolves to <c>AutoMapper.Profile</c>; (2) a method, local-function,
/// or lambda parameter whose type resolves to <c>AutoMapper.IMapperConfigurationExpression</c>; or
/// (3) an invocation whose resolved method is <c>AddAutoMapper</c> declared in the real
/// <c>AutoMapper</c> assembly.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Real-assembly resolution, not a syntax-only name match.</strong> All three shapes
/// require the resolved symbol's <see cref="IAssemblySymbol.Name"/> to equal exactly
/// <c>"AutoMapper"</c>, mirroring SK0025's <c>ContainingAssembly.Name</c> exact-match technique —
/// a syntax-only check on the simple name <c>"Profile"</c> would misfire against unrelated,
/// platform-owned types coincidentally named <c>Profile</c> elsewhere in this repo or a consuming
/// service (e.g. a user/tenant "profile" concept).
/// </para>
/// <para>
/// <strong>Mapster is deliberately NOT enforced.</strong> Mapster's runtime (reflection-based) and
/// source-generated adapter call syntax (<c>.Adapt&lt;T&gt;()</c>/<c>.BuildAdapter()</c>) is
/// IDENTICAL regardless of whether the companion <c>Mapster.SourceGenerator</c> package is
/// installed — there is no reliable syntactic or semantic-model discriminator between "this call
/// resolves to a hand-written runtime reflection path" and "this call resolves to a
/// source-generated implementation with the same public API." Rather than ship a rule with an
/// uncontrolled false-positive rate against a legitimate Mapster source-generated consumer, this
/// rule scopes to AutoMapper only, per this domain's established "narrow scope rather than ship
/// false positives" convention (mirrors SK0022/SK0024's own documented scope limits). Mapster usage
/// of either kind remains un-enforced by tooling — the root <c>CLAUDE.md</c> "What Goes Where"
/// guidance is the only mechanism naming Mapperly/hand-written mapping as the sanctioned choices.
/// </para>
/// <para>
/// Fires globally, no suppression namespace — AutoMapper has no legitimate call site anywhere on
/// this platform (unlike, e.g., SK0013's <c>SharedKernel.Communication.Rest</c> exemption for raw
/// <c>HttpClient</c> construction). Introduced WO-079 P-486. No
/// <c>SharedKernel.ArchitectureTests</c> counterpart — a per-compilation-unit source-level pattern
/// this Roslyn analyzer resolves completely on its own, mirroring SK0030's "no architecture-test
/// counterpart by design" precedent. Fully ungated: <c>SharedKernel.Analyzers</c> itself takes zero
/// new NuGet dependency (assembly identity is resolved via
/// <see cref="ISymbol.ContainingAssembly"/>, not a compile-time reference) — only
/// <c>SharedKernel.Analyzers.Tests</c> needs a test-only <c>PackageReference</c> to the real
/// <c>AutoMapper</c> package for fixture compilation.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ReflectionBasedObjectMapperUsageAnalyzer : AnalyzerBase
{
    private const string DiagnosticId = "SK0033";
    private const string AutoMapperAssemblyName = "AutoMapper";
    private const string ProfileTypeName = "Profile";
    private const string MapperConfigurationExpressionTypeName = "IMapperConfigurationExpression";
    private const string AddAutoMapperMethodName = "AddAutoMapper";

    /// <summary>The diagnostic descriptor for SK0033.</summary>
    public static readonly DiagnosticDescriptor Rule = CreateDescriptor(
        id: DiagnosticId,
        title: "Reflection-based object mapper (AutoMapper) usage",
        messageFormat: "'{0}' uses AutoMapper's reflection-based mapping API via {1}. Replace with a "
            + "Riok.Mapperly [Mapper] partial class (compile-time source-generated, zero runtime "
            + "reflection, AOT-clean) or hand-written mapping code, colocated in whichever "
            + "package/service owns the mapping direction.",
        category: Usage,
        defaultSeverity: DiagnosticSeverity.Warning,
        readmeAnchor: "sk0033-reflectionbasedobjectmapperusage"
    );

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSyntaxNodeAction(AnalyzeClassDeclaration, SyntaxKind.ClassDeclaration);
        context.RegisterSyntaxNodeAction(AnalyzeParameter, SyntaxKind.Parameter);
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    /// <summary>Shape (1) — a class declaration whose base type resolves to <c>AutoMapper.Profile</c>.</summary>
    private static void AnalyzeClassDeclaration(SyntaxNodeAnalysisContext context)
    {
        var classDeclaration = (ClassDeclarationSyntax)context.Node;

        if (classDeclaration.BaseList is null)
            return;

        foreach (var baseType in classDeclaration.BaseList.Types)
        {
            var typeInfo = context.SemanticModel.GetTypeInfo(baseType.Type, context.CancellationToken);

            if (!IsAutoMapperType(typeInfo.Type, ProfileTypeName))
                continue;

            context.ReportDiagnostic(
                Diagnostic.Create(
                    Rule,
                    baseType.GetLocation(),
                    classDeclaration.Identifier.Text,
                    "an AutoMapper.Profile base class"
                )
            );
            return;
        }
    }

    /// <summary>
    /// Shape (2) — a method, local-function, or lambda parameter whose type resolves to
    /// <c>AutoMapper.IMapperConfigurationExpression</c>.
    /// </summary>
    private static void AnalyzeParameter(SyntaxNodeAnalysisContext context)
    {
        var parameter = (ParameterSyntax)context.Node;

        if (parameter.Type is null)
            return;

        var typeInfo = context.SemanticModel.GetTypeInfo(parameter.Type, context.CancellationToken);

        if (!IsAutoMapperType(typeInfo.Type, MapperConfigurationExpressionTypeName))
            return;

        context.ReportDiagnostic(
            Diagnostic.Create(
                Rule,
                parameter.Type.GetLocation(),
                parameter.Identifier.Text,
                "an AutoMapper.IMapperConfigurationExpression parameter"
            )
        );
    }

    /// <summary>
    /// Shape (3) — an invocation whose resolved method is <c>AddAutoMapper</c> declared in the real
    /// <c>AutoMapper</c> assembly (its DI registration entry point, merged into the core package
    /// since AutoMapper v12).
    /// </summary>
    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        var symbolInfo = context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken);

        if (symbolInfo.Symbol is not IMethodSymbol methodSymbol)
            return;

        if (methodSymbol.Name != AddAutoMapperMethodName)
            return;

        if (methodSymbol.ContainingAssembly?.Name != AutoMapperAssemblyName)
            return;

        context.ReportDiagnostic(
            Diagnostic.Create(
                Rule,
                invocation.GetLocation(),
                AddAutoMapperMethodName,
                "an AutoMapper.AddAutoMapper(...) DI registration call"
            )
        );
    }

    private static bool IsAutoMapperType(ITypeSymbol? type, string expectedSimpleName) =>
        type is not null
        && type.Name == expectedSimpleName
        && type.ContainingAssembly?.Name == AutoMapperAssemblyName;
}

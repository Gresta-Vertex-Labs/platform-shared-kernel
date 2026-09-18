using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SharedKernel.Analyzers.Diagnostics;

/// <summary>
/// SK0002 — Fires on two independent shapes: (1) a <see cref="ParameterSyntax"/>,
/// <see cref="FieldDeclarationSyntax"/>, or <see cref="PropertyDeclarationSyntax"/> declared as one
/// of the four Microsoft feature-management evaluator interfaces
/// (<c>Microsoft.FeatureManagement.IFeatureManager</c>, <c>IVariantFeatureManager</c>,
/// <c>IFeatureManagerSnapshot</c>, or <c>IVariantFeatureManagerSnapshot</c>); or (2) a member-access
/// expression resolving to the static property <c>OpenFeature.Api.Instance</c>.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Why both shapes are banned.</strong> The platform's <c>SharedKernel.FeatureManagement</c>
/// package (P-555 redesign) wraps <c>Microsoft.FeatureManagement</c> behind the CNCF-standard
/// OpenFeature evaluator, <c>OpenFeature.IFeatureClient</c>, registered scoped by
/// <c>AddSharedKernelFeatureManagement(configuration)</c>. Reaching past that seam to a Microsoft
/// evaluator interface skips the ambient user/tenant targeting context, the per-request evaluation
/// consistency, the fail-safe defaults, and the telemetry hook the SharedKernel wiring adds — the
/// entire reason the package exists.
/// </para>
/// <para>
/// <strong><c>OpenFeature.Api.Instance</c> is independently dangerous, not merely redundant.</strong>
/// <c>AddSharedKernelFeatureManagement</c> registers an ISOLATED OpenFeature <c>Api</c> instance in
/// DI (<c>OpenFeature.Hosting</c> calls <c>OpenFeatureFactory.CreateIsolated()</c> under the hood),
/// so the process-global <c>Api.Instance</c> singleton has no provider attached to it at all. A
/// client obtained from <c>Api.Instance</c> silently reports the OpenFeature "No-op Provider" and
/// every flag evaluation returns its caller-supplied default — no exception, no log, just a
/// silently-wrong answer. This is flagged by the same rule as the four interfaces because both
/// mistakes have the identical fix: inject <c>OpenFeature.IFeatureClient</c> from DI instead.
/// </para>
/// <para>
/// <strong>Registration.</strong> Three <see cref="SyntaxKind"/> registrations for the declaration
/// shape — one each for <see cref="SyntaxKind.Parameter"/>, <see cref="SyntaxKind.FieldDeclaration"/>,
/// and <see cref="SyntaxKind.PropertyDeclaration"/> — plus a fourth for
/// <see cref="SyntaxKind.SimpleMemberAccessExpression"/> covering <c>Api.Instance</c> (with or
/// without the leading <c>OpenFeature.</c> qualification; both resolve to the same property symbol).
/// Every registration resolves against the <see cref="SemanticModel"/> rather than matching syntax
/// text, so a same-simple-named but unrelated type or property never false-positives.
/// </para>
/// <para>
/// <strong>Unresolved-symbol fallback.</strong> When the declared type genuinely fails to bind (a
/// missing package reference leaves the compilation degraded), the declaration shape falls back to a
/// syntax-only simple-name comparison against the four forbidden interfaces' short names. This is a
/// defensive fallback for an already-degraded compilation, not the primary path. The member-access
/// shape has no equivalent fallback — a member access with no resolvable symbol carries no reliable
/// syntax-only signal that distinguishes <c>Api.Instance</c> from an unrelated <c>Api.Instance</c>
/// on some other type, so it is silently skipped rather than risk a false positive.
/// </para>
/// <para>
/// <strong>Owning-package exemption.</strong> Neither shape fires inside a compilation whose
/// <see cref="Compilation.AssemblyName"/> is exactly <c>SharedKernel.FeatureManagement</c> — the
/// package's own internal OpenFeature provider adapter
/// (<c>Internal/MicrosoftFeatureManagementProvider.cs</c>) legitimately implements against
/// <c>Microsoft.FeatureManagement.IVariantFeatureManager</c> to bridge it into OpenFeature; that is
/// the one sanctioned bridging point and does not itself use <c>Api.Instance</c>.
/// </para>
/// <para>
/// <strong>Pass case:</strong> <c>OpenFeature.IFeatureClient</c> — a distinct type from every
/// forbidden shape — never matches on either the declaration path or the member-access path.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DirectMicrosoftFeatureManagerAnalyzer : AnalyzerBase
{
    private const string DiagnosticId = "SK0002";
    private const string ExemptedAssemblyName = "SharedKernel.FeatureManagement";

    private const string OpenFeatureApiInstanceMessage =
        "AddSharedKernelFeatureManagement registers an isolated OpenFeature Api instance in DI, so "
        + "Api.Instance has no provider attached and silently returns every flag's default.";

    private const string MicrosoftEvaluatorInterfaceMessage =
        "Bypassing IFeatureClient skips the ambient user/tenant targeting, per-request evaluation "
        + "consistency, fail-safe defaults and telemetry that SharedKernel.FeatureManagement adds.";

    private static readonly HashSet<string> ForbiddenFullNames = new(StringComparer.Ordinal)
    {
        "Microsoft.FeatureManagement.IFeatureManager",
        "Microsoft.FeatureManagement.IVariantFeatureManager",
        "Microsoft.FeatureManagement.IFeatureManagerSnapshot",
        "Microsoft.FeatureManagement.IVariantFeatureManagerSnapshot",
    };

    private static readonly HashSet<string> ForbiddenShortNames = new(StringComparer.Ordinal)
    {
        "IFeatureManager",
        "IVariantFeatureManager",
        "IFeatureManagerSnapshot",
        "IVariantFeatureManagerSnapshot",
    };

    private const string OpenFeatureApiInstanceFullName = "OpenFeature.Api.Instance";

    /// <summary>The diagnostic descriptor for SK0002.</summary>
    public static readonly DiagnosticDescriptor Rule = CreateDescriptor(
        id: DiagnosticId,
        title: "Direct Microsoft.FeatureManagement / ambient OpenFeature API usage",
        messageFormat: "Do not reference '{0}' directly. {1} Inject OpenFeature's 'OpenFeature.IFeatureClient' "
            + "and evaluate a 'SharedKernel.FeatureManagement.FeatureFlag<T>' instead.",
        category: Usage,
        defaultSeverity: DiagnosticSeverity.Warning,
        readmeAnchor: "sk0002-directmicrosoftfeaturemanagerusage"
    );

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        // Declaration shape — constructor/method parameters
        context.RegisterSyntaxNodeAction(AnalyzeParameter, SyntaxKind.Parameter);

        // Declaration shape — field declarations
        context.RegisterSyntaxNodeAction(AnalyzeFieldDeclaration, SyntaxKind.FieldDeclaration);

        // Declaration shape — property declarations
        context.RegisterSyntaxNodeAction(AnalyzePropertyDeclaration, SyntaxKind.PropertyDeclaration);

        // Ambient-API shape — OpenFeature.Api.Instance member access
        context.RegisterSyntaxNodeAction(
            AnalyzeMemberAccess,
            SyntaxKind.SimpleMemberAccessExpression
        );
    }

    private static void AnalyzeParameter(SyntaxNodeAnalysisContext context)
    {
        var parameter = (ParameterSyntax)context.Node;
        if (parameter.Type is null || IsExemptedAssembly(context.SemanticModel.Compilation))
            return;

        if (!TryGetForbiddenTypeName(parameter.Type, context.SemanticModel, out var forbiddenName))
            return;

        context.ReportDiagnostic(
            Diagnostic.Create(
                Rule,
                parameter.Type.GetLocation(),
                forbiddenName,
                MicrosoftEvaluatorInterfaceMessage
            )
        );
    }

    private static void AnalyzeFieldDeclaration(SyntaxNodeAnalysisContext context)
    {
        var field = (FieldDeclarationSyntax)context.Node;
        if (IsExemptedAssembly(context.SemanticModel.Compilation))
            return;

        if (!TryGetForbiddenTypeName(field.Declaration.Type, context.SemanticModel, out var forbiddenName))
            return;

        context.ReportDiagnostic(
            Diagnostic.Create(
                Rule,
                field.Declaration.Type.GetLocation(),
                forbiddenName,
                MicrosoftEvaluatorInterfaceMessage
            )
        );
    }

    private static void AnalyzePropertyDeclaration(SyntaxNodeAnalysisContext context)
    {
        var property = (PropertyDeclarationSyntax)context.Node;
        if (IsExemptedAssembly(context.SemanticModel.Compilation))
            return;

        if (!TryGetForbiddenTypeName(property.Type, context.SemanticModel, out var forbiddenName))
            return;

        context.ReportDiagnostic(
            Diagnostic.Create(
                Rule,
                property.Type.GetLocation(),
                forbiddenName,
                MicrosoftEvaluatorInterfaceMessage
            )
        );
    }

    /// <summary>
    /// Ambient-API shape — flags a member access resolving to the real
    /// <c>OpenFeature.Api.Instance</c> static property, regardless of whether it is written bare
    /// (<c>Api.Instance</c>) or fully qualified (<c>OpenFeature.Api.Instance</c>); both resolve to
    /// the identical property symbol.
    /// </summary>
    private static void AnalyzeMemberAccess(SyntaxNodeAnalysisContext context)
    {
        var memberAccess = (MemberAccessExpressionSyntax)context.Node;

        if (memberAccess.Name.Identifier.ValueText != "Instance")
            return;

        if (IsExemptedAssembly(context.SemanticModel.Compilation))
            return;

        var symbolInfo = context.SemanticModel.GetSymbolInfo(memberAccess, context.CancellationToken);

        if (symbolInfo.Symbol is not IPropertySymbol propertySymbol)
            return;

        if (propertySymbol.ToDisplayString() != OpenFeatureApiInstanceFullName)
            return;

        context.ReportDiagnostic(
            Diagnostic.Create(
                Rule,
                memberAccess.GetLocation(),
                OpenFeatureApiInstanceFullName,
                OpenFeatureApiInstanceMessage
            )
        );
    }

    private static bool TryGetForbiddenTypeName(
        TypeSyntax typeSyntax,
        SemanticModel semanticModel,
        out string forbiddenName
    )
    {
        var typeInfo = semanticModel.GetTypeInfo(typeSyntax);
        var symbol = typeInfo.Type;

        if (symbol is null)
        {
            // Fallback: syntax-only check for unresolved symbols
            var shortName = ExtractTypeName(typeSyntax);
            if (shortName is not null && ForbiddenShortNames.Contains(shortName))
            {
                forbiddenName = $"Microsoft.FeatureManagement.{shortName}";
                return true;
            }

            forbiddenName = string.Empty;
            return false;
        }

        var displayName = symbol.ToDisplayString();
        if (ForbiddenFullNames.Contains(displayName))
        {
            forbiddenName = displayName;
            return true;
        }

        forbiddenName = string.Empty;
        return false;
    }

    private static string? ExtractTypeName(TypeSyntax typeSyntax) =>
        typeSyntax switch
        {
            IdentifierNameSyntax id => id.Identifier.ValueText,
            QualifiedNameSyntax q => q.Right.Identifier.ValueText,
            _ => null,
        };

    /// <summary>
    /// Owning-package exemption — neither shape fires inside <c>SharedKernel.FeatureManagement</c>
    /// itself, whose internal OpenFeature provider adapter legitimately bridges the Microsoft
    /// evaluator interfaces.
    /// </summary>
    private static bool IsExemptedAssembly(Compilation compilation) =>
        compilation.AssemblyName == ExemptedAssemblyName;
}

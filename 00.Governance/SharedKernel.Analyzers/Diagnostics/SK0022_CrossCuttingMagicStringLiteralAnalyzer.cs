using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SharedKernel.Analyzers.Diagnostics;

/// <summary>
/// SK0022 — bans a raw string-literal token at any of four recognized cross-cutting call-site
/// shapes: an HTTP header indexer/<c>.Add</c>/<c>.TryAddWithoutValidation</c> call,
/// <c>Activity.SetBaggage</c>/<c>.SetTag</c>, <c>IConfiguration.GetSection</c>, and a
/// <c>ClaimsPrincipal</c>/<c>Claim</c> type comparison.
/// </summary>
/// <remarks>
/// <para>
/// <strong>One class, one diagnostic ID, four trigger shapes.</strong> All four shapes encode the
/// exact same underlying rule — "never a raw literal at a cross-cutting call site" — not four
/// distinct standards, so a single <see cref="DiagnosticDescriptor"/> covers all of them. This is
/// narrower than SK0020/SK0021's "one class, two IDs" precedent (<see cref="LoggingAuthoringStyleAnalyzer"/>).
/// </para>
/// <para>
/// <strong>Syntax-shape discriminator only.</strong> The rule flags the LITERAL SYNTAX SHAPE, never
/// a resolved value or declaring-class identity — mirroring the acceptance-critical generality
/// requirement already established for <c>NoBareHealthCheckLiteralWhereConstantsExistPredicate</c>
/// (WO-028 P-178). Any expression that is not itself a <see cref="LiteralExpressionSyntax"/> of
/// kind <see cref="SyntaxKind.StringLiteralExpression"/> at the checked position — an
/// <see cref="IdentifierNameSyntax"/>, a <see cref="MemberAccessExpressionSyntax"/>, or any other
/// non-literal expression — passes clean, regardless of which class declares the referenced field.
/// This implementation deliberately contains no specific constants-class name as a string literal
/// or type-name check anywhere in its trigger/exemption logic.
/// </para>
/// <para>
/// Each of the four shapes requires <see cref="SemanticModel.GetSymbolInfo(SyntaxNode, System.Threading.CancellationToken)"/>
/// (or <see cref="SemanticModel.GetTypeInfo(SyntaxNode, System.Threading.CancellationToken)"/> for
/// the element-access shape) to resolve the receiver/method/indexer to its EXACT containing type —
/// the same SK0020 discipline: a syntax-only simple-name match on <c>SetTag</c>/<c>GetSection</c>/
/// <c>FindFirst</c>/etc. would collide with unrelated types sharing those common method names.
/// </para>
/// <para>
/// <strong>No suppression namespace.</strong> SK0022 fires globally, like SK0014/SK0017–19 — not
/// with a suppression namespace like SK0001/SK0007/SK0013/SK0020/SK0021. A domain-local
/// constants-holder class already satisfies the rule anywhere it is referenced (it is a reference,
/// never a literal, at the call site) — there is no legitimate "exempt namespace" for a raw literal
/// at any of these four call-site shapes. Per-call-site suppression via
/// <c>#pragma warning disable SK0022</c> remains available; document the rationale inline.
/// </para>
/// <para>Introduced in WO-042 P-264.</para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class CrossCuttingMagicStringLiteralAnalyzer : AnalyzerBase
{
    private const string DiagnosticId = "SK0022";

    private const string HttpHeadersTypeName = "System.Net.Http.Headers.HttpHeaders";
    private const string HeaderDictionaryTypeName = "Microsoft.AspNetCore.Http.IHeaderDictionary";
    private const string ActivityTypeName = "System.Diagnostics.Activity";
    private const string ConfigurationTypeName = "Microsoft.Extensions.Configuration.IConfiguration";
    private const string ConfigurationExtensionsTypeName =
        "Microsoft.Extensions.Configuration.ConfigurationExtensions";
    private const string ClaimsPrincipalTypeName = "System.Security.Claims.ClaimsPrincipal";
    private const string ClaimsIdentityTypeName = "System.Security.Claims.ClaimsIdentity";
    private const string ClaimTypeName = "System.Security.Claims.Claim";

    /// <summary>The diagnostic descriptor for SK0022.</summary>
    public static readonly DiagnosticDescriptor Rule = CreateDescriptor(
        id: DiagnosticId,
        title: "Raw string literal at a cross-cutting call site",
        messageFormat: "Raw string literal at a cross-cutting call site. Declare a named constant "
            + "instead: use SharedKernel.Primitives.Propagation.WellKnownHeaders/WellKnownBaggageKeys "
            + "(01.Core) if this value is a platform-shared correlation/tenant identifier consumed "
            + "across multiple domains, or a domain-local static readonly/const constants class "
            + "(mirroring SecurityClaimTypes, WebhookSignatureHeaders, HubGroupNaming) if it is "
            + "specific to this package.",
        category: Usage,
        defaultSeverity: DiagnosticSeverity.Warning,
        readmeAnchor: "sk0022-crosscuttingmagicstringliteral"
    );

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSyntaxNodeAction(AnalyzeElementAccess, SyntaxKind.ElementAccessExpression);
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
        context.RegisterSyntaxNodeAction(
            AnalyzeBinaryEquality,
            SyntaxKind.EqualsExpression,
            SyntaxKind.NotEqualsExpression
        );
    }

    // -------------------------------------------------------------------------------------------
    // Shape 1 (indexer form): headers["X"] = ... / IHeaderDictionary["X"]
    // -------------------------------------------------------------------------------------------

    private static void AnalyzeElementAccess(SyntaxNodeAnalysisContext context)
    {
        var elementAccess = (ElementAccessExpressionSyntax)context.Node;

        if (elementAccess.ArgumentList.Arguments.Count != 1)
            return;

        if (!TryGetStringLiteral(elementAccess.ArgumentList.Arguments[0].Expression, out var literal))
            return;

        var typeInfo = context.SemanticModel.GetTypeInfo(elementAccess.Expression, context.CancellationToken);
        if (typeInfo.Type is not { } receiverType)
            return;

        if (IsHttpHeaderReceiverType(receiverType))
        {
            Report(context, literal);
        }
    }

    // -------------------------------------------------------------------------------------------
    // Shape 1 (method form): .Add(...) / .TryAddWithoutValidation(...)
    // Shape 2: Activity.SetBaggage(...) / .SetTag(...)
    // Shape 3: IConfiguration.GetSection(...)
    // Shape 4 (method form): ClaimsPrincipal/ClaimsIdentity HasClaim/FindFirst/FindAll, and
    // Claim.Type.Equals(...)
    // -------------------------------------------------------------------------------------------

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            return;

        var symbolInfo = context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken);
        if (symbolInfo.Symbol is not IMethodSymbol methodSymbol || methodSymbol.ContainingType is null)
            return;

        var containingTypeFullName = GetFullTypeName(methodSymbol.ContainingType);
        var methodName = methodSymbol.Name;

        // HTTP header .Add(...) / .TryAddWithoutValidation(...)
        if (
            (methodName is "Add" or "TryAddWithoutValidation")
            && containingTypeFullName is HttpHeadersTypeName or HeaderDictionaryTypeName
        )
        {
            ReportIfLiteralArgument(context, invocation, argumentIndex: 0);
            return;
        }

        // Activity.SetBaggage / .SetTag
        if (methodName is "SetBaggage" or "SetTag" && containingTypeFullName == ActivityTypeName)
        {
            ReportIfLiteralArgument(context, invocation, argumentIndex: 0);
            return;
        }

        // IConfiguration.GetSection (interface method, or its ConfigurationExtensions
        // static-extension-method overload)
        if (
            methodName == "GetSection"
            && containingTypeFullName is ConfigurationTypeName or ConfigurationExtensionsTypeName
        )
        {
            ReportIfLiteralArgument(context, invocation, argumentIndex: 0);
            return;
        }

        // ClaimsPrincipal/ClaimsIdentity — HasClaim / FindFirst / FindAll
        if (
            methodName is "HasClaim" or "FindFirst" or "FindAll"
            && containingTypeFullName is ClaimsPrincipalTypeName or ClaimsIdentityTypeName
        )
        {
            var stringParameterIndex = FindFirstStringParameterIndex(methodSymbol);
            if (stringParameterIndex is { } index)
            {
                ReportIfLiteralArgument(context, invocation, index);
            }
            return;
        }

        // Claim.Type.Equals("...")
        if (
            methodName == "Equals"
            && invocation.ArgumentList.Arguments.Count == 1
            && IsClaimTypePropertyAccess(context, memberAccess.Expression)
        )
        {
            ReportIfLiteralArgument(context, invocation, argumentIndex: 0);
        }
    }

    // -------------------------------------------------------------------------------------------
    // Shape 4 (binary form): claim.Type == "..." / "..." == claim.Type (and !=)
    // -------------------------------------------------------------------------------------------

    private static void AnalyzeBinaryEquality(SyntaxNodeAnalysisContext context)
    {
        var binary = (BinaryExpressionSyntax)context.Node;

        if (
            IsClaimTypePropertyAccess(context, binary.Left)
            && TryGetStringLiteral(binary.Right, out var rightLiteral)
        )
        {
            Report(context, rightLiteral);
            return;
        }

        if (
            IsClaimTypePropertyAccess(context, binary.Right)
            && TryGetStringLiteral(binary.Left, out var leftLiteral)
        )
        {
            Report(context, leftLiteral);
        }
    }

    // -------------------------------------------------------------------------------------------
    // Shared helpers
    // -------------------------------------------------------------------------------------------

    private static bool IsClaimTypePropertyAccess(SyntaxNodeAnalysisContext context, ExpressionSyntax expression)
    {
        if (expression is not MemberAccessExpressionSyntax memberAccess)
            return false;

        if (memberAccess.Name.Identifier.Text != "Type")
            return false;

        var symbolInfo = context.SemanticModel.GetSymbolInfo(memberAccess, context.CancellationToken);
        return symbolInfo.Symbol is IPropertySymbol { ContainingType: { } containingType }
            && GetFullTypeName(containingType) == ClaimTypeName;
    }

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="receiverType"/> IS, or inherits from,
    /// <see cref="HttpHeadersTypeName"/> (covers <c>HttpRequestHeaders</c>/<c>HttpResponseHeaders</c>/
    /// <c>HttpContentHeaders</c> via the base-type chain), or IS/implements
    /// <see cref="HeaderDictionaryTypeName"/>.
    /// </summary>
    private static bool IsHttpHeaderReceiverType(ITypeSymbol receiverType)
    {
        var current = receiverType;
        while (current is not null)
        {
            if (GetFullTypeName(current) == HttpHeadersTypeName)
                return true;
            current = current.BaseType;
        }

        if (GetFullTypeName(receiverType) == HeaderDictionaryTypeName)
            return true;

        foreach (var iface in receiverType.AllInterfaces)
        {
            if (GetFullTypeName(iface) == HeaderDictionaryTypeName)
                return true;
        }

        return false;
    }

    private static int? FindFirstStringParameterIndex(IMethodSymbol methodSymbol)
    {
        for (var i = 0; i < methodSymbol.Parameters.Length; i++)
        {
            if (methodSymbol.Parameters[i].Type.SpecialType == SpecialType.System_String)
                return i;
        }

        return null;
    }

    private static void ReportIfLiteralArgument(
        SyntaxNodeAnalysisContext context,
        InvocationExpressionSyntax invocation,
        int argumentIndex
    )
    {
        var arguments = invocation.ArgumentList.Arguments;
        if (argumentIndex < 0 || argumentIndex >= arguments.Count)
            return;

        if (TryGetStringLiteral(arguments[argumentIndex].Expression, out var literal))
        {
            Report(context, literal);
        }
    }

    private static bool TryGetStringLiteral(ExpressionSyntax expression, out LiteralExpressionSyntax literal)
    {
        if (expression is LiteralExpressionSyntax candidate && candidate.IsKind(SyntaxKind.StringLiteralExpression))
        {
            literal = candidate;
            return true;
        }

        literal = null!;
        return false;
    }

    private static void Report(SyntaxNodeAnalysisContext context, LiteralExpressionSyntax literal)
    {
        context.ReportDiagnostic(Diagnostic.Create(Rule, literal.GetLocation()));
    }

    private static string GetFullTypeName(ITypeSymbol type) =>
        type.ContainingNamespace is { IsGlobalNamespace: false }
            ? $"{type.ContainingNamespace.ToDisplayString()}.{type.Name}"
            : type.Name;
}

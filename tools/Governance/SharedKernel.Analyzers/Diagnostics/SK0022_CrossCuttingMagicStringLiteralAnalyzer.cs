using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SharedKernel.Analyzers.Diagnostics;

/// <summary>
/// SK0022 — bans a raw string-literal token at any of four recognized cross-cutting call-site
/// shapes: an HTTP header indexer/<c>.Add</c>/<c>.Append</c>/<c>.TryAddWithoutValidation</c> call,
/// <c>Activity.SetBaggage</c>/<c>.SetTag</c>/<c>.AddBaggage</c>/<c>.AddTag</c>,
/// <c>IConfiguration.GetSection</c>/<c>.GetRequiredSection</c>, and a
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
/// <strong>Receiver type, not declaring type.</strong> The header and configuration shapes are
/// matched on the type of the expression the method is called on, never on the type that declares
/// the method. On the real framework types the two differ: <c>IHeaderDictionary.Add</c> is inherited
/// from <c>IDictionary&lt;string, StringValues&gt;</c>, <c>headers.Append</c> is an extension method on
/// <c>HeaderDictionaryExtensions</c>, and <c>ConfigurationManager.GetSection</c> (what
/// <c>WebApplicationBuilder.Configuration</c> returns) is declared on the class. A declaring-type match
/// missed all three while passing its tests, because the in-compilation stubs declared the members
/// directly on the interfaces. Null-conditional calls (<c>Activity.Current?.SetTag(...)</c>) are
/// handled by resolving the receiver of the enclosing conditional access.
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

        if (!TryGetReceiver(invocation, out var receiver))
            return;

        var symbolInfo = context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken);
        if (symbolInfo.Symbol is not IMethodSymbol methodSymbol || methodSymbol.ContainingType is null)
            return;

        var containingTypeFullName = GetFullTypeName(methodSymbol.ContainingType);
        var methodName = methodSymbol.Name;

        // HTTP header .Add(...) / .Append(...) / .TryAddWithoutValidation(...), matched on the
        // receiver: the real IHeaderDictionary inherits Add and gets Append as an extension method.
        if (methodName is "Add" or "Append" or "TryAddWithoutValidation" && IsHttpHeaderReceiver(context, receiver))
        {
            ReportIfLiteralArgument(context, invocation, argumentIndex: 0);
            return;
        }

        // Activity.SetBaggage / .SetTag / .AddBaggage / .AddTag
        if (
            methodName is "SetBaggage" or "SetTag" or "AddBaggage" or "AddTag"
            && containingTypeFullName == ActivityTypeName
        )
        {
            ReportIfLiteralArgument(context, invocation, argumentIndex: 0);
            return;
        }

        // IConfiguration.GetSection / .GetRequiredSection, on any receiver that is or implements
        // IConfiguration (ConfigurationManager, IConfigurationRoot, IConfigurationSection, ...)
        if (
            methodName is "GetSection" or "GetRequiredSection"
            && (
                containingTypeFullName is ConfigurationTypeName or ConfigurationExtensionsTypeName
                || IsConfigurationReceiver(context, receiver)
            )
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
            && IsClaimTypePropertyAccess(context, receiver)
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

    /// <summary>
    /// Resolves the expression a method is invoked on: <c>x</c> in <c>x.M()</c>, and also <c>x</c> in
    /// <c>x?.M()</c>, where the invocation is a member binding inside a conditional access.
    /// </summary>
    private static bool TryGetReceiver(InvocationExpressionSyntax invocation, out ExpressionSyntax receiver)
    {
        switch (invocation.Expression)
        {
            case MemberAccessExpressionSyntax memberAccess:
                receiver = memberAccess.Expression;
                return true;

            case MemberBindingExpressionSyntax:
                for (var node = invocation.Parent; node is not null; node = node.Parent)
                {
                    if (node is ConditionalAccessExpressionSyntax conditional)
                    {
                        receiver = conditional.Expression;
                        return true;
                    }
                }
                break;
        }

        receiver = null!;
        return false;
    }

    private static bool IsHttpHeaderReceiver(SyntaxNodeAnalysisContext context, ExpressionSyntax receiver) =>
        context.SemanticModel.GetTypeInfo(receiver, context.CancellationToken).Type is { } type
        && IsHttpHeaderReceiverType(type);

    private static bool IsConfigurationReceiver(SyntaxNodeAnalysisContext context, ExpressionSyntax receiver)
    {
        if (context.SemanticModel.GetTypeInfo(receiver, context.CancellationToken).Type is not { } type)
            return false;

        if (GetFullTypeName(type) == ConfigurationTypeName)
            return true;

        foreach (var iface in type.AllInterfaces)
        {
            if (GetFullTypeName(iface) == ConfigurationTypeName)
                return true;
        }

        return false;
    }

    private static bool IsClaimTypePropertyAccess(SyntaxNodeAnalysisContext context, ExpressionSyntax expression)
    {
        SimpleNameSyntax? name = expression switch
        {
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name,
            MemberBindingExpressionSyntax memberBinding => memberBinding.Name,
            _ => null,
        };

        if (name?.Identifier.Text != "Type")
            return false;

        var symbolInfo = context.SemanticModel.GetSymbolInfo(expression, context.CancellationToken);
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

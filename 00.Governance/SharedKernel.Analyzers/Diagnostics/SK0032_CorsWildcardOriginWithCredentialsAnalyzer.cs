using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SharedKernel.Analyzers.Diagnostics;

/// <summary>
/// SK0032 — Fires when a <c>Microsoft.AspNetCore.Cors.Infrastructure.CorsPolicyBuilder</c>-typed
/// receiver has both an <c>AllowCredentials()</c> call and, anywhere in the same scope, either an
/// <c>AllowAnyOrigin()</c> call or a <c>SetIsOriginAllowed(...)</c> call whose lambda argument is
/// syntactically unconditional-true.
/// </summary>
/// <remarks>
/// <para>
/// Combining a wildcard/always-allow origin policy with <c>AllowCredentials()</c> is the classic
/// OWASP-catalogued CORS misconfiguration — most browsers already reject the combination at the
/// wire level, but ASP.NET Core's own <c>CorsService</c> only rejects it at request-handling time,
/// so a misconfigured policy fails silently per-request instead of failing fast at startup.
/// </para>
/// <para>
/// <strong>Receiver-type guard:</strong> a syntax-only name match on
/// <c>AllowAnyOrigin</c>/<c>AllowCredentials</c>/<c>SetIsOriginAllowed</c> would misfire against
/// any unrelated type coincidentally exposing same-named methods, so every receiver is confirmed
/// via <see cref="SemanticModel.GetTypeInfo(ExpressionSyntax, System.Threading.CancellationToken)"/>
/// to resolve to <c>Microsoft.AspNetCore.Cors.Infrastructure.CorsPolicyBuilder</c> before any
/// further check runs — joining the platform's semantic-model-assisted analyzer family (SK0011,
/// SK0015, SK0017–SK0020, SK0022, SK0024–SK0031).
/// </para>
/// <para>
/// <strong>Detection covers two call shapes, unified into a single technique:</strong> (a) a
/// single fluent invocation chain (<c>builder.AllowAnyOrigin().AllowCredentials()</c>), and
/// (b) separate statements against the same local variable/parameter/field within one method or
/// lambda body (<c>var p = new CorsPolicyBuilder(); p.AllowAnyOrigin(); p.AllowCredentials();</c>).
/// Both shapes reduce to the same check: starting from the <c>AllowCredentials()</c> call's
/// receiver, the fluent-invocation chain is unwrapped back to its ROOT expression (the base
/// identifier/field the chain started from, or an object-creation expression when the chain
/// starts with <c>new CorsPolicyBuilder()</c>). When that root resolves to a local, parameter, or
/// field symbol, the analyzer scans every invocation inside the nearest enclosing method/lambda
/// body for an <c>AllowAnyOrigin()</c>/unconditional-true <c>SetIsOriginAllowed(...)</c> call whose
/// OWN chain root resolves to the SAME symbol — this single scan naturally covers both the direct
/// fluent-chain shape (the immediately-preceding call in the same statement is itself found by
/// this scan) and the separate-statement shape. When the root does not resolve to a trackable
/// symbol (e.g. a bare <c>new CorsPolicyBuilder()</c> chain root, which has no stable identity to
/// search a wider scope for), detection falls back to checking only the immediate fluent chain
/// preceding <c>AllowCredentials()</c>.
/// </para>
/// <para>
/// <strong>Unconditional-true lambda detection (documented, intentional scope limit):</strong> the
/// <c>SetIsOriginAllowed</c> branch fires only when its single lambda argument is SYNTACTICALLY
/// unconditional-true — an expression-bodied lambda that is exactly the <c>true</c> literal, or a
/// block-bodied lambda consisting of exactly <c>return true;</c>. No data-flow or
/// constant-propagation analysis is performed — an indirect always-true path (e.g. a local
/// <c>const bool always = true; return always;</c>, or a call to a helper that always returns
/// <see langword="true"/>) is not caught. This mirrors this file's established
/// "pattern/presence check, not full reachability analysis" convention (SK0028,
/// <c>HealthCheckTagIntegrityRules</c>, <c>NoSecurityContextSingletonRegistrationPredicate</c>).
/// </para>
/// <para>
/// <strong>Scope limit:</strong> the same-symbol tracking is bounded to a single method/lambda
/// body — a builder reference passed into a separate helper method that calls
/// <c>AllowCredentials()</c> elsewhere is not followed across the method boundary. Also a
/// documented, intentional scope limit.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class CorsWildcardOriginWithCredentialsAnalyzer : AnalyzerBase
{
    private const string DiagnosticId = "SK0032";
    private const string AllowAnyOriginMethodName = "AllowAnyOrigin";
    private const string SetIsOriginAllowedMethodName = "SetIsOriginAllowed";
    private const string AllowCredentialsMethodName = "AllowCredentials";
    private const string CorsPolicyBuilderTypeName = "CorsPolicyBuilder";
    private const string CorsPolicyBuilderNamespace = "Microsoft.AspNetCore.Cors.Infrastructure";

    /// <summary>The diagnostic descriptor for SK0032.</summary>
    public static readonly DiagnosticDescriptor Rule = CreateDescriptor(
        id: DiagnosticId,
        title: "CORS policy combines a wildcard/always-allow origin with AllowCredentials",
        messageFormat: "CorsPolicyBuilder combines AllowCredentials() with {0}() in the same scope "
            + "— this is rejected only at request time by ASP.NET Core's CorsService, not at "
            + "startup; replace {0}() with an explicit origin allowlist",
        category: Security,
        defaultSeverity: DiagnosticSeverity.Warning,
        readmeAnchor: "sk0032-corswildcardoriginwithcredentials"
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

        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            return;

        if (!string.Equals(
                memberAccess.Name.Identifier.Text,
                AllowCredentialsMethodName,
                System.StringComparison.Ordinal))
        {
            return;
        }

        if (invocation.ArgumentList.Arguments.Count != 0)
            return;

        var receiverExpression = memberAccess.Expression;

        if (!IsCorsPolicyBuilderReceiver(context, receiverExpression))
            return;

        var directChain = new List<InvocationExpressionSyntax>();
        var root = UnwrapChainAndCollect(receiverExpression, directChain);

        var dangerousMethodName = FindDangerousOriginCall(context, root, directChain);

        if (dangerousMethodName is null)
            return;

        context.ReportDiagnostic(
            Diagnostic.Create(Rule, invocation.GetLocation(), dangerousMethodName));
    }

    /// <summary>
    /// Determines whether <paramref name="root"/> — the chain-root expression of an
    /// <c>AllowCredentials()</c> call — is paired, in the same scope, with a dangerous
    /// wildcard/always-allow origin call. Returns the matched method name
    /// (<see cref="AllowAnyOriginMethodName"/> or <see cref="SetIsOriginAllowedMethodName"/>), or
    /// <see langword="null"/> when no match is found.
    /// </summary>
    private static string? FindDangerousOriginCall(
        SyntaxNodeAnalysisContext context,
        ExpressionSyntax root,
        List<InvocationExpressionSyntax> directChain)
    {
        var rootSymbol = context.SemanticModel.GetSymbolInfo(root, context.CancellationToken).Symbol;

        if (rootSymbol is not null
            && rootSymbol.Kind is SymbolKind.Local or SymbolKind.Parameter or SymbolKind.Field)
        {
            var scope = GetEnclosingScope(root);

            if (scope is not null)
            {
                foreach (var candidate in scope.DescendantNodes().OfType<InvocationExpressionSyntax>())
                {
                    var dangerousMethodName = ClassifyIfDangerous(context, candidate);

                    if (dangerousMethodName is null)
                        continue;

                    // candidate's own chain root must resolve to the SAME symbol as AllowCredentials()'s
                    // chain root — this is what distinguishes "same builder, different statement" from
                    // an unrelated CorsPolicyBuilder-typed local elsewhere in the same scope.
                    var candidateMemberAccess = (MemberAccessExpressionSyntax)candidate.Expression;
                    var candidateChain = new List<InvocationExpressionSyntax>();
                    var candidateRoot = UnwrapChainAndCollect(
                        candidateMemberAccess.Expression, candidateChain);
                    var candidateRootSymbol = context.SemanticModel
                        .GetSymbolInfo(candidateRoot, context.CancellationToken)
                        .Symbol;

                    if (candidateRootSymbol is not null
                        && SymbolEqualityComparer.Default.Equals(candidateRootSymbol, rootSymbol))
                    {
                        return dangerousMethodName;
                    }
                }

                return null;
            }
        }

        // Fallback: the chain root does not resolve to a trackable local/parameter/field symbol
        // (e.g. a bare `new CorsPolicyBuilder()` chain root has no stable identity to search a
        // wider scope for) — only the DIRECT fluent chain preceding AllowCredentials() can be
        // checked.
        foreach (var chained in directChain)
        {
            var dangerousMethodName = ClassifyIfDangerous(context, chained);

            if (dangerousMethodName is not null)
                return dangerousMethodName;
        }

        return null;
    }

    /// <summary>
    /// Classifies <paramref name="invocation"/> as a dangerous wildcard/always-allow origin call
    /// (returning its method name) when it is an <c>AllowAnyOrigin()</c> call, or a
    /// <c>SetIsOriginAllowed(...)</c> call whose single lambda argument is syntactically
    /// unconditional-true — both confirmed to be invoked on a <c>CorsPolicyBuilder</c> receiver.
    /// Returns <see langword="null"/> for every other invocation.
    /// </summary>
    private static string? ClassifyIfDangerous(
        SyntaxNodeAnalysisContext context,
        InvocationExpressionSyntax invocation)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            return null;

        var methodName = memberAccess.Name.Identifier.Text;

        if (string.Equals(methodName, AllowAnyOriginMethodName, System.StringComparison.Ordinal))
        {
            return IsCorsPolicyBuilderReceiver(context, memberAccess.Expression)
                ? AllowAnyOriginMethodName
                : null;
        }

        if (string.Equals(methodName, SetIsOriginAllowedMethodName, System.StringComparison.Ordinal))
        {
            if (!IsCorsPolicyBuilderReceiver(context, memberAccess.Expression))
                return null;

            var arguments = invocation.ArgumentList.Arguments;

            if (arguments.Count != 1)
                return null;

            return IsUnconditionalTrueLambda(arguments[0].Expression)
                ? SetIsOriginAllowedMethodName
                : null;
        }

        return null;
    }

    /// <summary>
    /// Walks backward through a fluent invocation chain (<c>X.Method1().Method2()</c>-shaped
    /// receiver expressions), collecting every chained <see cref="InvocationExpressionSyntax"/>
    /// into <paramref name="chainedInvocations"/>, and returns the ultimate root expression the
    /// chain started from (an identifier, field access, <c>this</c>, or an object-creation
    /// expression such as <c>new CorsPolicyBuilder()</c>).
    /// </summary>
    private static ExpressionSyntax UnwrapChainAndCollect(
        ExpressionSyntax expression,
        List<InvocationExpressionSyntax> chainedInvocations)
    {
        var current = expression;

        while (current is InvocationExpressionSyntax invocation
            && invocation.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            chainedInvocations.Add(invocation);
            current = memberAccess.Expression;
        }

        return current;
    }

    /// <summary>
    /// Returns the nearest enclosing method/constructor/accessor/local-function/lambda body syntax
    /// node containing <paramref name="node"/> — the "single method/lambda body" scope boundary
    /// same-symbol tracking is bounded to.
    /// </summary>
    private static SyntaxNode? GetEnclosingScope(SyntaxNode node)
    {
        foreach (var ancestor in node.Ancestors())
        {
            switch (ancestor)
            {
                case AnonymousFunctionExpressionSyntax:
                case LocalFunctionStatementSyntax:
                case BaseMethodDeclarationSyntax:
                case AccessorDeclarationSyntax:
                    return ancestor;
            }
        }

        return null;
    }

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="argumentExpression"/> is a lambda whose
    /// body is SYNTACTICALLY unconditional-true — an expression body that is exactly the
    /// <c>true</c> literal, or a block body consisting of exactly <c>return true;</c>.
    /// </summary>
    private static bool IsUnconditionalTrueLambda(ExpressionSyntax argumentExpression)
    {
        ExpressionSyntax? expressionBody;
        BlockSyntax? blockBody;

        switch (argumentExpression)
        {
            case SimpleLambdaExpressionSyntax simpleLambda:
                expressionBody = simpleLambda.ExpressionBody;
                blockBody = simpleLambda.Block;
                break;
            case ParenthesizedLambdaExpressionSyntax parenthesizedLambda:
                expressionBody = parenthesizedLambda.ExpressionBody;
                blockBody = parenthesizedLambda.Block;
                break;
            default:
                return false;
        }

        if (expressionBody is LiteralExpressionSyntax expressionLiteral
            && expressionLiteral.IsKind(SyntaxKind.TrueLiteralExpression))
        {
            return true;
        }

        if (blockBody is { Statements.Count: 1 }
            && blockBody.Statements[0] is ReturnStatementSyntax
            {
                Expression: LiteralExpressionSyntax returnLiteral,
            }
            && returnLiteral.IsKind(SyntaxKind.TrueLiteralExpression))
        {
            return true;
        }

        return false;
    }

    private static bool IsCorsPolicyBuilderReceiver(
        SyntaxNodeAnalysisContext context,
        ExpressionSyntax receiverExpression)
    {
        var typeInfo = context.SemanticModel.GetTypeInfo(receiverExpression, context.CancellationToken);

        return IsCorsPolicyBuilderType(typeInfo.Type);
    }

    private static bool IsCorsPolicyBuilderType(ITypeSymbol? type) =>
        type is not null
        && string.Equals(type.Name, CorsPolicyBuilderTypeName, System.StringComparison.Ordinal)
        && type.ContainingNamespace is not null
        && string.Equals(
            type.ContainingNamespace.ToDisplayString(),
            CorsPolicyBuilderNamespace,
            System.StringComparison.Ordinal);
}

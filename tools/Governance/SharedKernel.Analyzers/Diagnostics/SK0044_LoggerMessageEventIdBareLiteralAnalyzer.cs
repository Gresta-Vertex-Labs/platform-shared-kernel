using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SharedKernel.Analyzers.Diagnostics;

/// <summary>
/// SK0044 — a <c>[LoggerMessage]</c> whose <c>EventId</c> is a bare numeric literal.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Why.</strong> The kernel gives every package a 100-wide EventId sub-block and builds each id from a
/// <c>{Package}LoggingEventIds</c> constant over <c>LoggingEventIdRanges</c>. A literal <c>1234</c> hides which block the id
/// belongs to and lets two packages collide silently. An architecture test cannot tell a literal from a named constant (a
/// <c>const int</c> folds into the attribute blob), so this is an analyzer: only syntax shape separates the two.
/// </para>
/// <para>
/// <strong>Off by default.</strong> Literal ids are legitimate in a consuming service, so the descriptor is
/// <c>isEnabledByDefault: false</c> (default severity still Warning). The kernel's self-analysis configuration enables it;
/// a service opts in with <c>dotnet_diagnostic.SK0044.severity = warning</c>.
/// </para>
/// <para>
/// <strong>Trigger.</strong> A <c>Microsoft.Extensions.Logging.LoggerMessageAttribute</c> whose <c>EventId</c> argument
/// (named <c>EventId =</c>, or the positional <c>eventId</c> parameter, named or not) is a
/// <see cref="LiteralExpressionSyntax"/> once parentheses are removed. An identifier, a member access or any other
/// expression (<c>Base + 1</c>) passes: literal-versus-constant rules discriminate on syntax shape, never on the resolved
/// value. Generated trees (<c>.g.cs</c>, an <c>&lt;auto-generated</c> header) are skipped.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class LoggerMessageEventIdBareLiteralAnalyzer : AnalyzerBase
{
    private const string DiagnosticId = "SK0044";
    private const string LoggerMessageAttributeMetadataName = "Microsoft.Extensions.Logging.LoggerMessageAttribute";
    private const string EventIdName = "EventId";

    /// <summary>The diagnostic descriptor for SK0044.</summary>
    public static readonly DiagnosticDescriptor Rule = CreateDescriptor(
        id: DiagnosticId,
        title: "[LoggerMessage] EventId is a bare numeric literal",
        messageFormat: "The EventId of this [LoggerMessage] is a bare numeric literal. Build it from the package's "
            + "{{Package}}LoggingEventIds constant, which is const arithmetic over LoggingEventIdRanges "
            + "(SharedKernel.Primitives.Logging), so the id visibly belongs to the package's sub-block.",
        category: Design,
        defaultSeverity: DiagnosticSeverity.Warning,
        readmeAnchor: "sk0044-loggermessageeventidbareliteral",
        isEnabledByDefault: false
    );

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        // Not GeneratedCodeAnalysisFlags.None: the [LoggerMessage] generator adds a [GeneratedCode] part to the very
        // partial method the attribute sits on, and the analyzer driver then treats the whole merged method symbol as
        // generated and skips every node action inside its declaration, attribute included. The generator's output never
        // carries a [LoggerMessage] attribute, so generated trees are skipped by hand instead (IsGeneratedTree).
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze | GeneratedCodeAnalysisFlags.ReportDiagnostics);
        context.EnableConcurrentExecution();

        context.RegisterSyntaxNodeAction(AnalyzeAttribute, SyntaxKind.Attribute);
    }

    private static void AnalyzeAttribute(SyntaxNodeAnalysisContext context)
    {
        var attribute = (AttributeSyntax)context.Node;

        if (IsGeneratedTree(attribute.SyntaxTree))
            return;

        if (attribute.ArgumentList is not { Arguments.Count: > 0 } argumentList)
            return;

        // Cheap pre-filter before paying for symbol resolution.
        var simpleName = GetRightmostName(attribute.Name);
        if (simpleName is not ("LoggerMessage" or "LoggerMessageAttribute"))
            return;

        if (context.SemanticModel.GetSymbolInfo(attribute, context.CancellationToken).Symbol is not IMethodSymbol constructor)
            return;

        var attributeType = constructor.ContainingType;
        if (attributeType is null || GetMetadataName(attributeType) != LoggerMessageAttributeMetadataName)
            return;

        var eventIdExpression = FindEventIdExpression(argumentList, constructor);
        if (eventIdExpression is null)
            return;

        if (Unwrap(eventIdExpression) is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.NumericLiteralExpression))
        {
            context.ReportDiagnostic(Diagnostic.Create(Rule, literal.GetLocation()));
        }
    }

    private static ExpressionSyntax? FindEventIdExpression(AttributeArgumentListSyntax argumentList, IMethodSymbol constructor)
    {
        var arguments = argumentList.Arguments;

        for (var i = 0; i < arguments.Count; i++)
        {
            var argument = arguments[i];

            // [LoggerMessage(EventId = 1)]
            if (argument.NameEquals is { } nameEquals)
            {
                if (nameEquals.Name.Identifier.Text == EventIdName)
                    return argument.Expression;
                continue;
            }

            // [LoggerMessage(eventId: 1, ...)]
            if (argument.NameColon is { } nameColon)
            {
                if (string.Equals(nameColon.Name.Identifier.Text, "eventId", System.StringComparison.OrdinalIgnoreCase))
                    return argument.Expression;
                continue;
            }

            // [LoggerMessage(1, LogLevel.Information, "...")]: the positional argument bound to the int 'eventId' parameter.
            if (
                i < constructor.Parameters.Length
                && constructor.Parameters[i].Type.SpecialType == SpecialType.System_Int32
                && string.Equals(constructor.Parameters[i].Name, "eventId", System.StringComparison.OrdinalIgnoreCase)
            )
            {
                return argument.Expression;
            }
        }

        return null;
    }

    /// <summary>
    /// A tree is generated when its file name says so (<c>.g.cs</c>, <c>.generated.cs</c>, <c>.designer.cs</c>) or its
    /// leading comment carries the <c>&lt;auto-generated</c> marker, the same conventions Roslyn's own check uses.
    /// </summary>
    private static bool IsGeneratedTree(SyntaxTree tree)
    {
        var path = tree.FilePath;
        if (
            !string.IsNullOrEmpty(path)
            && (
                path.EndsWith(".g.cs", System.StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".generated.cs", System.StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".designer.cs", System.StringComparison.OrdinalIgnoreCase)
            )
        )
        {
            return true;
        }

        foreach (var trivia in tree.GetRoot().GetLeadingTrivia())
        {
            if (!trivia.IsKind(SyntaxKind.SingleLineCommentTrivia) && !trivia.IsKind(SyntaxKind.MultiLineCommentTrivia))
                continue;

            var text = trivia.ToString();
            if (
                text.IndexOf("<auto-generated", System.StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("<autogenerated", System.StringComparison.OrdinalIgnoreCase) >= 0
            )
            {
                return true;
            }
        }

        return false;
    }

    private static ExpressionSyntax Unwrap(ExpressionSyntax expression)
    {
        while (expression is ParenthesizedExpressionSyntax parenthesized)
            expression = parenthesized.Expression;
        return expression;
    }

    private static string GetRightmostName(NameSyntax name) =>
        name switch
        {
            QualifiedNameSyntax qualified => qualified.Right.Identifier.Text,
            AliasQualifiedNameSyntax alias => alias.Name.Identifier.Text,
            SimpleNameSyntax simple => simple.Identifier.Text,
            _ => string.Empty,
        };

    private static string GetMetadataName(ITypeSymbol type) =>
        type.ContainingNamespace is { IsGlobalNamespace: false } ns ? $"{ns.ToDisplayString()}.{type.MetadataName}" : type.MetadataName;
}

using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SharedKernel.Analyzers.Diagnostics;

/// <summary>
/// SK0038 / SK0039 — checks the <c>[IntegrationEvent]</c> attribute that gives every concrete integration event
/// its wire name and schema version.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Why.</strong> <c>SharedKernel.Contracts</c>' <c>EventEnvelope.Wrap</c> and
/// <c>IntegrationEventDescriptor.For</c> refuse an integration event whose attribute is missing or invalid, but
/// only at run time, the first time the type is published or consumed. These diagnostics move that failure to
/// compile time for the cases a syntax check can see.
/// </para>
/// <para>
/// <strong>SK0038 — IntegrationEventMissingAttribute.</strong> Fires on a non-<see langword="abstract"/>
/// <see cref="ClassDeclarationSyntax"/> or <see cref="RecordDeclarationSyntax"/> whose own base list names
/// <c>IIntegrationEvent</c> and that carries no attribute simply named <c>IntegrationEvent</c> or
/// <c>IntegrationEventAttribute</c>. Reported on the type name.
/// </para>
/// <para>
/// <strong>SK0039 — InvalidIntegrationEventAttribute.</strong> Fires on such a type whose attribute is present
/// but declares, as literals, a wire name that breaks the name rule (1 to 128 lowercase ASCII letters and digits,
/// in segments separated by a single <c>.</c>, <c>-</c> or <c>_</c>, with no leading or trailing separator) or a
/// <c>Version</c> below 1. Reported on the offending attribute argument. A name or version given as anything
/// other than a literal (a constant, <c>nameof</c>, an interpolated string) is not evaluated; the run-time check
/// still covers it.
/// </para>
/// <para>
/// <strong>Detection style.</strong> Syntax-only and name-based, like SK0009: no semantic model is used, so a
/// consuming service or a test fixture can declare its own local <c>IIntegrationEvent</c> interface and
/// <c>IntegrationEventAttribute</c>. Each partial declaration is checked on its own, so the part that lists
/// <c>IIntegrationEvent</c> must carry the attribute. Abstract types are exempt, as are types that implement
/// the interface only through a base type, and structs.
/// </para>
/// <para>
/// The name rule mirrors <c>IntegrationEventDescriptor</c>'s own validation exactly; keep the two in step.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class IntegrationEventAttributeAnalyzer : AnalyzerBase
{
    private const string MissingAttributeId = "SK0038";
    private const string InvalidAttributeId = "SK0039";

    private const string IIntegrationEventSimpleName = "IIntegrationEvent";
    private const string IntegrationEventAttributeName = "IntegrationEvent";
    private const string IntegrationEventAttributeFullName = "IntegrationEventAttribute";
    private const string NameParameterName = "name";
    private const string VersionPropertyName = "Version";
    private const int MaxNameLength = 128;

    /// <summary>The diagnostic descriptor for SK0038.</summary>
    public static readonly DiagnosticDescriptor MissingAttributeRule = CreateDescriptor(
        id: MissingAttributeId,
        title: "Integration event missing [IntegrationEvent] attribute",
        messageFormat: "Type '{0}' implements IIntegrationEvent but has no [IntegrationEvent] attribute, so publishing or consuming it throws at run time — add [IntegrationEvent(\"context.event-name\", Version = N)] to declare its wire name and schema version",
        category: Design,
        defaultSeverity: DiagnosticSeverity.Warning,
        readmeAnchor: "sk0038-integrationeventmissingattribute"
    );

    /// <summary>The diagnostic descriptor for SK0039.</summary>
    public static readonly DiagnosticDescriptor InvalidAttributeRule = CreateDescriptor(
        id: InvalidAttributeId,
        title: "Invalid [IntegrationEvent] attribute",
        messageFormat: "Type '{0}' has an invalid [IntegrationEvent] attribute: {1}",
        category: Design,
        defaultSeverity: DiagnosticSeverity.Warning,
        readmeAnchor: "sk0039-invalidintegrationeventattribute"
    );

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(MissingAttributeRule, InvalidAttributeRule);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterSyntaxNodeAction(
            AnalyzeTypeDeclaration,
            SyntaxKind.ClassDeclaration,
            SyntaxKind.RecordDeclaration
        );
    }

    private static void AnalyzeTypeDeclaration(SyntaxNodeAnalysisContext context)
    {
        var typeDecl = (TypeDeclarationSyntax)context.Node;

        if (typeDecl.Modifiers.Any(SyntaxKind.AbstractKeyword))
            return;

        if (!ImplementsIIntegrationEvent(typeDecl))
            return;

        var typeName = typeDecl.Identifier.Text;

        var attribute = FindIntegrationEventAttribute(typeDecl);
        if (attribute is null)
        {
            context.ReportDiagnostic(
                Diagnostic.Create(MissingAttributeRule, typeDecl.Identifier.GetLocation(), typeName)
            );
            return;
        }

        if (attribute.ArgumentList is null)
            return;

        foreach (var argument in attribute.ArgumentList.Arguments)
        {
            if (argument.NameEquals is { } nameEquals)
            {
                if (nameEquals.Name.Identifier.Text == VersionPropertyName
                    && TryGetIntegerLiteral(argument.Expression, out var version)
                    && version < 1)
                {
                    context.ReportDiagnostic(
                        Diagnostic.Create(
                            InvalidAttributeRule,
                            argument.GetLocation(),
                            typeName,
                            $"Version is {version}, but versions start at 1"
                        )
                    );
                }

                continue;
            }

            if (!IsNameArgument(attribute.ArgumentList, argument))
                continue;

            if (argument.Expression is LiteralExpressionSyntax literal
                && literal.IsKind(SyntaxKind.StringLiteralExpression)
                && !IsValidName(literal.Token.ValueText))
            {
                context.ReportDiagnostic(
                    Diagnostic.Create(
                        InvalidAttributeRule,
                        argument.GetLocation(),
                        typeName,
                        $"the name '{literal.Token.ValueText}' is invalid — a name is 1 to {MaxNameLength} lowercase ASCII letters and digits, in segments separated by a single '.', '-' or '_', such as 'orders.order-placed'"
                    )
                );
            }
        }
    }

    // The name is the first positional argument, or an argument written as `name: "..."`.
    private static bool IsNameArgument(AttributeArgumentListSyntax argumentList, AttributeArgumentSyntax argument)
    {
        if (argument.NameColon is { } nameColon)
            return nameColon.Name.Identifier.Text == NameParameterName;

        var firstPositional = argumentList.Arguments.FirstOrDefault(a => a.NameEquals is null && a.NameColon is null);
        return firstPositional == argument;
    }

    private static bool TryGetIntegerLiteral(ExpressionSyntax expression, out long value)
    {
        value = 0;

        var negate = false;
        if (expression is PrefixUnaryExpressionSyntax unary)
        {
            if (unary.IsKind(SyntaxKind.UnaryMinusExpression))
                negate = true;
            else if (!unary.IsKind(SyntaxKind.UnaryPlusExpression))
                return false;

            expression = unary.Operand;
        }

        if (expression is not LiteralExpressionSyntax literal
            || !literal.IsKind(SyntaxKind.NumericLiteralExpression))
        {
            return false;
        }

        switch (literal.Token.Value)
        {
            case int i:
                value = i;
                break;
            case long l:
                value = l;
                break;
            case uint ui:
                value = ui;
                break;
            default:
                return false;
        }

        if (negate)
            value = -value;

        return true;
    }

    private static bool IsValidName(string name)
    {
        if (string.IsNullOrEmpty(name) || name.Length > MaxNameLength)
            return false;

        var previousWasSeparator = true;
        foreach (var c in name)
        {
            if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9'))
            {
                previousWasSeparator = false;
            }
            else if ((c == '.' || c == '-' || c == '_') && !previousWasSeparator)
            {
                previousWasSeparator = true;
            }
            else
            {
                return false;
            }
        }

        return !previousWasSeparator;
    }

    private static bool ImplementsIIntegrationEvent(TypeDeclarationSyntax typeDecl)
    {
        if (typeDecl.BaseList is null)
            return false;

        foreach (var baseType in typeDecl.BaseList.Types)
        {
            if (GetSimpleTypeName(baseType.Type) == IIntegrationEventSimpleName)
                return true;
        }

        return false;
    }

    private static AttributeSyntax? FindIntegrationEventAttribute(TypeDeclarationSyntax typeDecl)
    {
        foreach (var attributeList in typeDecl.AttributeLists)
        {
            foreach (var attribute in attributeList.Attributes)
            {
                var attrName = GetSimpleTypeName(attribute.Name);
                if (string.Equals(attrName, IntegrationEventAttributeName, StringComparison.Ordinal)
                    || string.Equals(attrName, IntegrationEventAttributeFullName, StringComparison.Ordinal))
                {
                    return attribute;
                }
            }
        }

        return null;
    }

    private static string GetSimpleTypeName(TypeSyntax typeSyntax) =>
        typeSyntax switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.Text,
            GenericNameSyntax generic => generic.Identifier.Text,
            QualifiedNameSyntax qualified => qualified.Right.Identifier.Text,
            AliasQualifiedNameSyntax aliasQualified => aliasQualified.Name.Identifier.Text,
            _ => typeSyntax.ToString(),
        };
}

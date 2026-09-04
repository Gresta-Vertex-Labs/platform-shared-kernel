using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SharedKernel.Analyzers.Diagnostics;

/// <summary>
/// SK0034 — Fires when a class/record/struct declares, as DIRECT (non-inherited) members, both a
/// <see langword="decimal"/>/<see langword="decimal"/><c>?</c>-typed member whose identifier ends
/// with one of "Amount", "Price", "Total", "Balance", AND a <see langword="string"/>/
/// <see langword="string"/><c>?</c>-typed member whose identifier ends with one of "Currency",
/// "CurrencyCode" — a heuristic nudge toward <c>03.Domain</c>'s <c>Money</c> value object instead of
/// a raw decimal-plus-string pair that can drift out of sync or silently mix currencies.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The platform's first ADVISORY-ONLY rule — permanently so.</strong> Every prior
/// Warning-severity rule in this registry is either already enforced at Warning pending a future
/// escalation to Error, or a permanent platform-wide prohibition deliberately kept at Warning. This
/// rule has NO escalation path to Error, ever: its detection technique (same-type suffix
/// co-occurrence) cannot meet the near-zero false-positive bar every Error-severity rule on this
/// platform requires. Do not escalate this rule to Error severity in any future phase without a
/// fresh design review.
/// </para>
/// <para>
/// <strong>Syntax-only detection — no <see cref="SemanticModel"/> needed.</strong> Both
/// <see langword="decimal"/> and <see langword="string"/> are BCL keyword types resolvable purely
/// from <see cref="PredefinedTypeSyntax"/>/<see cref="NullableTypeSyntax"/> syntax, so no symbol
/// resolution is required — a deliberate choice that also lets this rule run against source that
/// does not yet compile against a real <c>Money</c> reference.
/// </para>
/// <para>
/// <strong>Scope:</strong> only DIRECT <see cref="PropertyDeclarationSyntax"/>/
/// <see cref="FieldDeclarationSyntax"/> members of a <see langword="class"/>/<see langword="record"/>/
/// <see langword="struct"/> declaration are scanned — never an <see langword="interface"/>
/// (contract-only signatures generate excessive, low-value noise) and never inherited members (a
/// base-type property should not cause every derived type to re-flag). A positional record's
/// primary-constructor parameter list is likewise out of scope — only genuine property/field
/// declarations are inspected.
/// </para>
/// <para>
/// <strong>Self-exemption:</strong> a type whose own identifier is exactly <c>"Money"</c> never
/// fires, regardless of its members — defensive hygiene even though <c>03.Domain</c>'s real
/// <c>Money</c> type's <c>Currency</c> property is typed <c>Currency</c> (a value object), not
/// <see langword="string"/>, so it would not structurally trigger anyway.
/// </para>
/// <para>
/// <strong>Suppression:</strong> the standard built-in Roslyn mechanism
/// (<c>#pragma warning disable SK0034</c>, or a <c>.editorconfig</c> severity override) — no bespoke
/// suppression attribute exists. A legitimate case exists whenever the pair is a deliberate
/// wire-format/read-model choice (e.g. a <c>04.Contracts</c> DTO or a <c>06.Persistence</c> Dapper
/// projection intentionally avoiding a rich domain type at a serialization boundary).
/// </para>
/// <para>
/// No <c>SharedKernel.ArchitectureTests</c> counterpart — a per-compilation-unit source-level
/// heuristic, mirroring SK0030's/SK0033's "no architecture-test counterpart by design" precedent.
/// Introduced WO-066 P-442; depends on <c>03.Domain</c> P-439 (<c>Money</c>) only for this rule's OWN
/// remediation message to name a real, shipped type — the detection logic itself references no
/// compiled <c>SharedKernel.Domain</c> type.
/// </para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AmountCurrencyPairAdvisoryAnalyzer : AnalyzerBase
{
    private const string DiagnosticId = "SK0034";
    private const string DecimalKeyword = "decimal";
    private const string StringKeyword = "string";
    private const string SelfExemptTypeName = "Money";

    private static readonly ImmutableArray<string> AmountSuffixes = ImmutableArray.Create(
        "Amount",
        "Price",
        "Total",
        "Balance"
    );

    private static readonly ImmutableArray<string> CurrencySuffixes = ImmutableArray.Create(
        "Currency",
        "CurrencyCode"
    );

    /// <summary>The diagnostic descriptor for SK0034.</summary>
    public static readonly DiagnosticDescriptor Rule = CreateDescriptor(
        id: DiagnosticId,
        title: "Raw decimal amount + string currency-code pair",
        messageFormat: "'{0}' declares a decimal-shaped amount member ({1}) alongside a "
            + "string-shaped currency-code member ({2}). Consider replacing this pair with "
            + "SharedKernel.Domain.ValueObjects.Money, which enforces ISO 4217 minor-unit-correct "
            + "rounding and rejects cross-currency arithmetic. This is an advisory nudge — suppress "
            + "with a one-line comment naming the reason when this is a deliberate wire-format/"
            + "read-model choice.",
        category: Advisory,
        defaultSeverity: DiagnosticSeverity.Warning,
        readmeAnchor: "sk0034-amountcurrencypaircoupling"
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
            AnalyzeTypeDeclaration,
            SyntaxKind.ClassDeclaration,
            SyntaxKind.StructDeclaration,
            SyntaxKind.RecordDeclaration,
            SyntaxKind.RecordStructDeclaration
        );
    }

    private static void AnalyzeTypeDeclaration(SyntaxNodeAnalysisContext context)
    {
        var typeDeclaration = (TypeDeclarationSyntax)context.Node;

        if (string.Equals(typeDeclaration.Identifier.Text, SelfExemptTypeName, StringComparison.Ordinal))
            return;

        var amountMembers = new List<string>();
        var currencyMembers = new List<string>();

        foreach (var member in typeDeclaration.Members)
        {
            switch (member)
            {
                case PropertyDeclarationSyntax property:
                    Classify(property.Type, property.Identifier.Text, amountMembers, currencyMembers);
                    break;

                case FieldDeclarationSyntax field:
                    foreach (var declarator in field.Declaration.Variables)
                    {
                        Classify(
                            field.Declaration.Type,
                            declarator.Identifier.Text,
                            amountMembers,
                            currencyMembers
                        );
                    }
                    break;
            }
        }

        if (amountMembers.Count == 0 || currencyMembers.Count == 0)
            return;

        context.ReportDiagnostic(
            Diagnostic.Create(
                Rule,
                typeDeclaration.Identifier.GetLocation(),
                typeDeclaration.Identifier.Text,
                string.Join(", ", amountMembers),
                string.Join(", ", currencyMembers)
            )
        );
    }

    private static void Classify(
        TypeSyntax memberType,
        string identifierText,
        List<string> amountMembers,
        List<string> currencyMembers
    )
    {
        var keyword = GetPredefinedKeyword(memberType);

        if (keyword is null)
            return;

        if (keyword == DecimalKeyword && EndsWithAny(identifierText, AmountSuffixes))
        {
            amountMembers.Add(identifierText);
        }
        else if (keyword == StringKeyword && EndsWithAny(identifierText, CurrencySuffixes))
        {
            currencyMembers.Add(identifierText);
        }
    }

    /// <summary>
    /// Returns the BCL predefined-type keyword text (<c>"decimal"</c>/<c>"string"</c>/etc.) for
    /// <paramref name="typeSyntax"/>, unwrapping a single <see cref="NullableTypeSyntax"/> layer
    /// (<c>decimal?</c>/<c>string?</c>) first. Returns <see langword="null"/> for any non-predefined
    /// type syntax (a user-defined type, a generic type, an array, etc.).
    /// </summary>
    private static string? GetPredefinedKeyword(TypeSyntax typeSyntax)
    {
        var innerType = typeSyntax is NullableTypeSyntax nullableType ? nullableType.ElementType : typeSyntax;

        return innerType is PredefinedTypeSyntax predefinedType ? predefinedType.Keyword.Text : null;
    }

    private static bool EndsWithAny(string identifierText, ImmutableArray<string> suffixes)
    {
        foreach (var suffix in suffixes)
        {
            if (identifierText.EndsWith(suffix, StringComparison.Ordinal))
                return true;
        }

        return false;
    }
}

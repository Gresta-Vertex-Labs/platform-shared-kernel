using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SharedKernel.Analyzers.Diagnostics;

/// <summary>
/// Abstract base class for all SharedKernel Roslyn diagnostic analyzers.
/// Provides category constants and a factory method for creating <see cref="DiagnosticDescriptor"/>
/// instances with a consistent <see cref="DiagnosticDescriptor.HelpLinkUri"/> wired to the
/// tools/Governance/README.md rule anchor.
/// </summary>
public abstract class AnalyzerBase : DiagnosticAnalyzer
{
    /// <summary>Category constant for usage-pattern diagnostics (SK0001, SK0002).</summary>
    protected const string Usage = "Usage";

    /// <summary>Category constant for design-pattern diagnostics (SK0003–SK0006).</summary>
    protected const string Design = "Design";

    /// <summary>
    /// Category constant for security-relevant diagnostics (SK0032). Introduced WO-062/P-410 —
    /// the first Roslyn analyzer whose registry entry documents a "Security" category rather than
    /// "Usage"/"Design" (mirrors the "Security" category label already used in prose for the
    /// non-analyzer SK0301-series architecture-test predicates).
    /// </summary>
    protected const string Security = "Security";

    /// <summary>
    /// Category constant for the platform's first ADVISORY-ONLY diagnostic category (SK0034).
    /// Unlike every other Warning-severity category in this registry — which is either already
    /// enforced at Warning pending a future escalation to Error, or a permanent platform-wide
    /// prohibition deliberately kept at Warning — a rule in this category has NO escalation path to
    /// Error, ever: it is a heuristic nudge toward a better pattern, not a prohibition of a bad one.
    /// Introduced WO-066/P-442.
    /// </summary>
    protected const string Advisory = "Advisory";

    private const string HelpLinkBase =
        "https://github.com/gresta-vertex-labs/platform-shared-kernel/blob/main/tools/Governance/README.md";

    /// <summary>
    /// Creates a <see cref="DiagnosticDescriptor"/> with the <see cref="DiagnosticDescriptor.HelpLinkUri"/>
    /// automatically set to the rule anchor in the 00.Governance README.
    /// </summary>
    /// <param name="id">The SK diagnostic ID (e.g., "SK0001").</param>
    /// <param name="title">Short title for the diagnostic.</param>
    /// <param name="messageFormat">Message format string (may include {0} placeholders).</param>
    /// <param name="category">Use <see cref="Usage"/> or <see cref="Design"/>.</param>
    /// <param name="defaultSeverity">The default <see cref="DiagnosticSeverity"/> for this rule.</param>
    /// <param name="readmeAnchor">
    /// The README anchor fragment for the rule, e.g., <c>"sk0001-directdatetimeusage"</c>.
    /// The final URI is <c>{HelpLinkBase}#{readmeAnchor}</c>.
    /// </param>
    /// <returns>A fully configured <see cref="DiagnosticDescriptor"/>.</returns>
    protected static DiagnosticDescriptor CreateDescriptor(
        string id,
        string title,
        string messageFormat,
        string category,
        DiagnosticSeverity defaultSeverity,
        string readmeAnchor
    ) =>
        new DiagnosticDescriptor(
            id: id,
            title: title,
            messageFormat: messageFormat,
            category: category,
            defaultSeverity: defaultSeverity,
            isEnabledByDefault: true,
            helpLinkUri: $"{HelpLinkBase}#{readmeAnchor}"
        );
}

using System.Text;

namespace SharedKernel.ArchitectureTests.Observability;

/// <summary>
/// The result of one or more observability assertions: the findings, and the facts the IL analysis could not verify.
/// </summary>
/// <remarks>
/// An assertion returns a report and never throws by itself; <see cref="ObservabilityEnforcement.Apply"/> decides what a
/// non-empty report means. <see cref="Unverifiable"/> entries (for example a metric unit loaded from a field) are a limit of IL
/// analysis, not violations: they are listed and never fail.
/// </remarks>
public sealed class ObservabilityReport
{
    /// <summary>A report with no findings.</summary>
    public static ObservabilityReport Empty { get; } = new([], []);

    /// <summary>Creates a report.</summary>
    /// <param name="findings">The violations.</param>
    /// <param name="unverifiable">The facts that could not be verified from IL.</param>
    public ObservabilityReport(IEnumerable<ObservabilityFinding> findings, IEnumerable<ObservabilityFinding> unverifiable)
    {
        ArgumentNullException.ThrowIfNull(findings);
        ArgumentNullException.ThrowIfNull(unverifiable);
        Findings = [.. findings.Distinct().OrderBy(f => f.Domain, StringComparer.Ordinal).ThenBy(f => f.Rule, StringComparer.Ordinal).ThenBy(f => f.Assembly, StringComparer.Ordinal).ThenBy(f => f.Site, StringComparer.Ordinal)];
        Unverifiable = [.. unverifiable.Distinct().OrderBy(f => f.Domain, StringComparer.Ordinal).ThenBy(f => f.Assembly, StringComparer.Ordinal).ThenBy(f => f.Site, StringComparer.Ordinal)];
    }

    /// <summary>Gets the violations, ordered by domain, rule, assembly and site.</summary>
    public IReadOnlyList<ObservabilityFinding> Findings { get; }

    /// <summary>Gets the facts the IL analysis could not verify (listed, never a failure).</summary>
    public IReadOnlyList<ObservabilityFinding> Unverifiable { get; }

    /// <summary>Gets a value indicating whether there is no finding (unverifiable entries do not count).</summary>
    public bool IsClean => Findings.Count == 0;

    /// <summary>Combines reports into one; identical findings (the same <c>unscanned</c> assembly seen by every assertion) appear once.</summary>
    /// <param name="reports">The reports to combine.</param>
    /// <returns>The combined report.</returns>
    public static ObservabilityReport Combine(params ObservabilityReport[] reports)
    {
        ArgumentNullException.ThrowIfNull(reports);
        return new ObservabilityReport(reports.SelectMany(r => r.Findings), reports.SelectMany(r => r.Unverifiable));
    }

    /// <summary>Counts findings per domain and rule.</summary>
    /// <returns>Domain, then rule, then count; both levels ordered.</returns>
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> CountsByDomain() =>
        Findings
            .GroupBy(f => f.Domain, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyDictionary<string, int>)g.GroupBy(f => f.Rule, StringComparer.Ordinal)
                    .OrderBy(r => r.Key, StringComparer.Ordinal)
                    .ToDictionary(r => r.Key, r => r.Count(), StringComparer.Ordinal),
                StringComparer.Ordinal);

    /// <summary>Renders the per-domain, per-rule table (one row per domain and rule) and the unverifiable count.</summary>
    /// <returns>A Markdown table.</returns>
    public string Render()
    {
        var builder = new StringBuilder();
        builder.AppendLine("| Domain | Rule | Findings |");
        builder.AppendLine("| --- | --- | ---: |");

        foreach (var (domain, rules) in CountsByDomain())
        {
            foreach (var (rule, count) in rules)
                builder.Append("| ").Append(domain).Append(" | ").Append(rule).Append(" | ").Append(count).AppendLine(" |");
        }

        if (IsClean)
            builder.AppendLine("| (none) | | 0 |");

        builder.Append("Total findings: ").Append(Findings.Count).Append("; unverifiable: ").Append(Unverifiable.Count).Append('.');
        return builder.ToString();
    }

    /// <summary>Renders every finding, one line per site, grouped by domain.</summary>
    /// <returns>The detail lines, or an empty string when clean.</returns>
    public string RenderSites()
    {
        var builder = new StringBuilder();
        foreach (var group in Findings.GroupBy(f => f.Domain, StringComparer.Ordinal))
        {
            builder.Append("## ").AppendLine(group.Key);
            foreach (var finding in group)
                builder.Append("- [").Append(finding.Rule).Append("] ").Append(finding.Assembly).Append(' ').Append(finding.Site).Append(": ").AppendLine(finding.Detail);
        }

        if (Unverifiable.Count > 0)
        {
            builder.AppendLine("## unverifiable");
            foreach (var entry in Unverifiable)
                builder.Append("- ").Append(entry.Assembly).Append(' ').Append(entry.Site).Append(": ").AppendLine(entry.Detail);
        }

        return builder.ToString();
    }
}

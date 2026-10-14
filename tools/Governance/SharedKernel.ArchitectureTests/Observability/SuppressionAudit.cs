using System.Text.RegularExpressions;

namespace SharedKernel.ArchitectureTests.Observability;

/// <summary>
/// One file the suppression audit reads.
/// </summary>
/// <param name="RepositoryRelativePath">The path relative to the repository root, with forward slashes.</param>
/// <param name="Text">The full text of the file.</param>
public sealed record SuppressionSource(string RepositoryRelativePath, string Text);

/// <summary>
/// Finds every suppression of an observability rule that its owning domain has not justified.
/// </summary>
/// <remarks>
/// A suppression is a <c>NoWarn</c> or <c>WarningsNotAsErrors</c> element in a project or props file, a severity below <c>error</c> in an
/// <c>.editorconfig</c> or globalconfig, a <c>#pragma warning disable</c> or a <c>SuppressMessage</c>, in each case naming one of the
/// caller's rule ids. It is justified when the <c>CLAUDE.md</c> of the owning domain (the longest folder prefix in the domain registry)
/// names both the rule id and the file (its repository path, its path inside the domain folder, its file name or its type name, which is the file stem). An unjustified one is a finding with the rule id <c>suppression.unjustified</c>. The caller
/// chooses the files (the production code under <c>src/</c>), the rule ids and the domain texts; nothing here lists a domain or a rule.
/// </remarks>
public static partial class SuppressionAudit
{
    /// <summary>The finding rule id of a suppression no domain brain justifies.</summary>
    public const string RuleId = "suppression.unjustified";

    /// <summary>Finds the unjustified suppressions.</summary>
    /// <param name="sources">The files to read.</param>
    /// <param name="registry">The domain registry.</param>
    /// <param name="domainBrains">The text of each domain CLAUDE.md, keyed by domain id.</param>
    /// <param name="ruleIds">The rule ids whose suppression is audited, for example every <c>SK</c> id and <c>CA2200</c>.</param>
    /// <returns>A report with one finding per unjustified suppression.</returns>
    public static ObservabilityReport Find(
        IReadOnlyCollection<SuppressionSource> sources,
        DomainRegistry registry,
        IReadOnlyDictionary<string, string> domainBrains,
        IReadOnlySet<string> ruleIds)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(domainBrains);
        ArgumentNullException.ThrowIfNull(ruleIds);

        var findings = new List<ObservabilityFinding>();
        foreach (var source in sources)
        {
            var path = source.RepositoryRelativePath.Replace('\\', '/');
            var domain = registry.Find(path);
            var domainId = domain?.Id ?? DomainRegistry.UnknownDomain;
            var brain = domain is not null && domainBrains.TryGetValue(domain.Id, out var text) ? text.Replace('\\', '/') : string.Empty;
            var relativeToDomain = domain is not null && path.StartsWith(domain.Folder + "/", StringComparison.OrdinalIgnoreCase) ? path[(domain.Folder.Length + 1)..] : path;

            foreach (var (id, line) in Suppressions(path, source.Text, ruleIds))
            {
                var named = brain.Contains(id, StringComparison.Ordinal)
                    && (brain.Contains(path, StringComparison.OrdinalIgnoreCase) || brain.Contains(relativeToDomain, StringComparison.OrdinalIgnoreCase)
                        || brain.Contains(Path.GetFileName(path), StringComparison.OrdinalIgnoreCase) || brain.Contains(Path.GetFileNameWithoutExtension(path), StringComparison.Ordinal));
                if (!named)
                {
                    findings.Add(new ObservabilityFinding(
                        RuleId,
                        domainId,
                        path,
                        $"{Path.GetFileName(path)}:{line}",
                        $"{id} is suppressed, but the CLAUDE.md of {domainId} does not name this file and the rule in its Known Limitations."));
                }
            }
        }

        return new ObservabilityReport(findings, []);
    }

    /// <summary>Finds the unjustified suppressions and throws one aggregated exception when there is any.</summary>
    /// <param name="sources">The files to read.</param>
    /// <param name="registry">The domain registry.</param>
    /// <param name="domainBrains">The text of each domain CLAUDE.md, keyed by domain id.</param>
    /// <param name="ruleIds">The rule ids whose suppression is audited.</param>
    /// <param name="output">Where the table goes; the console when <see langword="null"/>.</param>
    /// <returns>The report.</returns>
    /// <exception cref="InvalidOperationException">At least one unjustified suppression.</exception>
    public static ObservabilityReport Assert(
        IReadOnlyCollection<SuppressionSource> sources,
        DomainRegistry registry,
        IReadOnlyDictionary<string, string> domainBrains,
        IReadOnlySet<string> ruleIds,
        TextWriter? output = null)
    {
        var report = Find(sources, registry, domainBrains, ruleIds);
        ObservabilityEnforcement.Apply(report, output);
        return report;
    }

    private static IEnumerable<(string Id, int Line)> Suppressions(string path, string text, IReadOnlySet<string> ruleIds)
    {
        var name = Path.GetFileName(path);
        var extension = Path.GetExtension(path);

        if (extension.Equals(".cs", StringComparison.OrdinalIgnoreCase))
        {
            foreach (Match m in PragmaDisable().Matches(text))
            {
                foreach (var hit in Ids(m.Groups["ids"], text, ruleIds))
                    yield return hit;
            }

            foreach (Match m in SuppressMessage().Matches(text))
            {
                foreach (var hit in Ids(m.Groups["args"], text, ruleIds))
                    yield return hit;
            }
        }
        else if (extension is ".csproj" or ".props" or ".targets")
        {
            foreach (Match m in WarningElement().Matches(text))
            {
                foreach (var hit in Ids(m.Groups["value"], text, ruleIds))
                    yield return hit;
            }
        }
        else if (name.Equals(".editorconfig", StringComparison.OrdinalIgnoreCase) || extension is ".editorconfig" or ".globalconfig")
        {
            foreach (Match m in SeverityLine().Matches(text))
            {
                var id = m.Groups["id"].Value;
                if (ruleIds.Contains(id) && !m.Groups["severity"].Value.Equals("error", StringComparison.OrdinalIgnoreCase))
                    yield return (id, LineOf(text, m.Index));
            }
        }
    }

    private static IEnumerable<(string Id, int Line)> Ids(Group group, string text, IReadOnlySet<string> ruleIds)
    {
        foreach (Match id in RuleIdToken().Matches(group.Value))
        {
            if (ruleIds.Contains(id.Value))
                yield return (id.Value, LineOf(text, group.Index + id.Index));
        }
    }

    private static int LineOf(string text, int index)
    {
        var line = 1;
        for (var i = 0; i < index && i < text.Length; i++)
        {
            if (text[i] == '\n')
                line++;
        }

        return line;
    }

    [GeneratedRegex(@"\b(?:SK\d{4}|CA\d{4})\b", RegexOptions.CultureInvariant)]
    private static partial Regex RuleIdToken();

    [GeneratedRegex(@"^[ \t]*#[ \t]*pragma[ \t]+warning[ \t]+disable\b(?<ids>[^\r\n]*)", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex PragmaDisable();

    [GeneratedRegex(@"SuppressMessage\s*\((?<args>[^)]*)\)", RegexOptions.CultureInvariant)]
    private static partial Regex SuppressMessage();

    [GeneratedRegex(@"<(?:NoWarn|WarningsNotAsErrors)\b[^>]*>(?<value>[^<]*)</(?:NoWarn|WarningsNotAsErrors)>", RegexOptions.CultureInvariant)]
    private static partial Regex WarningElement();

    [GeneratedRegex(@"^[ \t]*dotnet_diagnostic\.(?<id>\w+)\.severity[ \t]*=[ \t]*(?<severity>\w+)", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex SeverityLine();
}

namespace SharedKernel.Linter.Tests;

/// <summary>
/// A minimal EditorConfig reader, sufficient to assert the shipped file's internal coherence.
/// </summary>
/// <remarks>
/// Deliberately hand-rolled rather than pulled from a package. The questions these tests ask are
/// about the literal keys present in one known file — which naming rules reference which symbol
/// groups, which sections set which values — not about EditorConfig's glob-matching or
/// precedence semantics, which are the compiler's job and not something to re-implement or
/// re-test here.
/// </remarks>
internal static class EditorConfigFile
{
    /// <summary>One entry as written in the file, tagged with the section it appeared under.</summary>
    /// <param name="Section">The section glob, or the empty string for the preamble.</param>
    /// <param name="Key">The key, lower-cased and trimmed.</param>
    /// <param name="Value">The value, trimmed.</param>
    internal readonly record struct Entry(string Section, string Key, string Value);

    internal static string Text { get; } = LinterPackage.EditorConfigText();

    /// <summary>
    /// Every key/value pair in the file, in declaration order, tagged with its section.
    /// </summary>
    internal static IReadOnlyList<Entry> AllSectionEntries()
    {
        var entries = new List<Entry>();
        var section = string.Empty;

        foreach (var raw in Text.Split('\n'))
        {
            var line = raw.Trim();

            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith(';'))
            {
                continue;
            }

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                section = line[1..^1];
                continue;
            }

            var separator = line.IndexOf('=');
            if (separator < 0)
            {
                continue;
            }

            entries.Add(
                new Entry(
                    section,
                    line[..separator].Trim().ToLowerInvariant(),
                    line[(separator + 1)..].Trim()
                )
            );
        }

        return entries;
    }

    /// <summary>The distinct section globs declared in the file, in declaration order.</summary>
    internal static IReadOnlyList<string> SectionNames() =>
        AllSectionEntries()
            .Select(entry => entry.Section)
            .Where(name => name.Length > 0)
            .Distinct()
            .ToArray();

    /// <summary>
    /// The effective key/value pairs for one section glob, later declarations winning.
    /// </summary>
    /// <param name="section">The section glob exactly as written, e.g. <c>*.md</c>.</param>
    internal static IReadOnlyDictionary<string, string> Section(string section)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var entry in AllSectionEntries().Where(entry => entry.Section == section))
        {
            values[entry.Key] = entry.Value;
        }

        return values;
    }

    /// <summary><c>dotnet_naming_rule.NAME.PROPERTY = VALUE</c>, grouped by rule name.</summary>
    internal static IReadOnlyDictionary<
        string,
        IReadOnlyDictionary<string, string>
    > NamingRules() => Grouped("dotnet_naming_rule.");

    /// <summary><c>dotnet_naming_symbols.NAME.PROPERTY = VALUE</c>, grouped by group name.</summary>
    internal static IReadOnlyDictionary<
        string,
        IReadOnlyDictionary<string, string>
    > NamingSymbolGroups() => Grouped("dotnet_naming_symbols.");

    /// <summary><c>dotnet_naming_style.NAME.PROPERTY = VALUE</c>, grouped by style name.</summary>
    internal static IReadOnlyDictionary<
        string,
        IReadOnlyDictionary<string, string>
    > NamingStyles() => Grouped("dotnet_naming_style.");

    /// <summary>
    /// <c>dotnet_diagnostic.ID.severity = VALUE</c> pairs, keyed by diagnostic id. A later
    /// declaration wins, matching how the compiler resolves a repeated key.
    /// </summary>
    internal static IReadOnlyDictionary<string, string> DiagnosticSeverities()
    {
        const string prefix = "dotnet_diagnostic.";
        const string suffix = ".severity";
        var severities = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var entry in AllSectionEntries())
        {
            if (
                !entry.Key.StartsWith(prefix, StringComparison.Ordinal)
                || !entry.Key.EndsWith(suffix, StringComparison.Ordinal)
            )
            {
                continue;
            }

            var id = entry.Key[prefix.Length..^suffix.Length];
            severities[id] = entry.Value;
        }

        return severities;
    }

    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Grouped(
        string prefix
    )
    {
        var grouped = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);

        foreach (var entry in AllSectionEntries())
        {
            if (!entry.Key.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            // NAME.PROPERTY — split on the LAST dot, since a name may itself contain dots.
            var remainder = entry.Key[prefix.Length..];
            var lastDot = remainder.LastIndexOf('.');
            if (lastDot <= 0)
            {
                continue;
            }

            var name = remainder[..lastDot];
            var property = remainder[(lastDot + 1)..];

            if (!grouped.TryGetValue(name, out var properties))
            {
                properties = new Dictionary<string, string>(StringComparer.Ordinal);
                grouped[name] = properties;
            }

            properties[property] = entry.Value;
        }

        return grouped.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyDictionary<string, string>)pair.Value,
            StringComparer.Ordinal
        );
    }
}

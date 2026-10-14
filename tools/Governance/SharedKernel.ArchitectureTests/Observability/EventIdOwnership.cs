using System.Globalization;
using System.Text.RegularExpressions;

namespace SharedKernel.ArchitectureTests.Observability;

/// <summary>
/// An EventId range declared for one or more assemblies in a domain <c>CLAUDE.md</c> <c>## Logging</c> table.
/// </summary>
/// <param name="Assemblies">The assemblies that own the range. More than one means a package family shares a sub-block (the base
/// package at offsets 1-49, its Adapters at 50-99 are separate entries; several Adapters share their entry).</param>
/// <param name="Start">The first EventId of the range.</param>
/// <param name="End">The last EventId of the range.</param>
/// <param name="DomainId">The domain whose <c>CLAUDE.md</c> declares it.</param>
public sealed record EventIdOwnershipEntry(IReadOnlyList<string> Assemblies, int Start, int End, string DomainId);

/// <summary>
/// The EventId ranges the domain <c>CLAUDE.md</c> files declare, read from their <c>## Logging</c> tables.
/// </summary>
/// <remarks>
/// <para>
/// Nothing is listed in code: a table row that names a package in a <c>Package</c> or <c>Owner</c> column and a range in a
/// <c>Sub-block</c> or <c>Range</c> column declares the range. The range is written absolutely (<c>6100–6199</c>) or relative to
/// the domain block (<c>+100 – +199</c>). The package cell holds package names as full names (<c>SharedKernel.X</c>), as the part after
/// <c>SharedKernel.</c> (<c>Caching.Redis.Core</c>), with a leading dot (<c>.Pipeline</c>) or as the last segment (<c>Oidc</c>); words are
/// matched against the assemblies of that domain only, and a word that names no assembly is ignored. A parenthetical
/// <c>(+ .Adapter, .Other at 50-99)</c> declares a family: the row's package owns offsets 0-49 of the sub-block and the adapters 50-99.
/// </para>
/// <para>
/// A row with no parseable range (a dash, a single id, a sentence) declares nothing. An assembly with no declared range is checked
/// against the default instead: all its ids in one 100-wide sub-block that no other assembly uses.
/// </para>
/// </remarks>
public sealed partial class EventIdOwnership
{
    private readonly List<EventIdOwnershipEntry> _entries;

    private EventIdOwnership(List<EventIdOwnershipEntry> entries) => _entries = entries;

    /// <summary>Gets every declared range, deduplicated.</summary>
    public IReadOnlyList<EventIdOwnershipEntry> Entries => _entries;

    /// <summary>Gets the ranges declared for one assembly.</summary>
    /// <param name="assemblyName">The assembly name.</param>
    /// <returns>Its entries, or an empty list when it declares none.</returns>
    public IReadOnlyList<EventIdOwnershipEntry> For(string assemblyName) =>
        [.. _entries.Where(e => e.Assemblies.Contains(assemblyName, StringComparer.OrdinalIgnoreCase))];

    /// <summary>Reads the ownership tables of the given domain <c>CLAUDE.md</c> files.</summary>
    /// <param name="registry">The domain registry (supplies each domain's block for relative ranges).</param>
    /// <param name="claudeFiles">Each domain id with the full text of its <c>CLAUDE.md</c>.</param>
    /// <param name="assemblyDomains">Every assembly name with the id of the domain it belongs to.</param>
    /// <returns>The declared ranges.</returns>
    public static EventIdOwnership Parse(
        DomainRegistry registry,
        IEnumerable<(string DomainId, string Markdown)> claudeFiles,
        IReadOnlyDictionary<string, string> assemblyDomains)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(claudeFiles);
        ArgumentNullException.ThrowIfNull(assemblyDomains);

        var entries = new List<EventIdOwnershipEntry>();
        foreach (var (domainId, markdown) in claudeFiles)
        {
            var domain = registry.Domains.FirstOrDefault(d => d.Id == domainId);
            if (domain is null)
                continue;

            var assemblies = assemblyDomains.Where(p => p.Value == domainId).Select(p => p.Key).ToList();
            foreach (var table in Tables(LoggingSection(markdown)))
                entries.AddRange(ReadTable(table, domain, assemblies));
        }

        var distinct = entries
            .GroupBy(e => (string.Join('|', e.Assemblies.Order(StringComparer.OrdinalIgnoreCase)), e.Start, e.End))
            .Select(g => g.First())
            .OrderBy(e => e.Start)
            .ToList();
        return new EventIdOwnership(distinct);
    }

    private static string LoggingSection(string markdown)
    {
        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        var inSection = false;
        var section = new List<string>();
        foreach (var line in lines)
        {
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                if (inSection)
                    break;
                inSection = line.TrimEnd().Equals("## Logging", StringComparison.Ordinal);
                continue;
            }

            if (inSection)
                section.Add(line);
        }

        return string.Join('\n', section);
    }

    private static IEnumerable<List<string>> Tables(string section)
    {
        var current = new List<string>();
        foreach (var line in section.Split('\n'))
        {
            if (line.TrimStart().StartsWith('|'))
            {
                current.Add(line.Trim());
                continue;
            }

            if (current.Count > 0)
                yield return current;
            current = [];
        }

        if (current.Count > 0)
            yield return current;
    }

    private static IEnumerable<EventIdOwnershipEntry> ReadTable(List<string> table, DomainInfo domain, List<string> assemblies)
    {
        if (table.Count < 3)
            yield break;

        var header = Cells(table[0]).Select(c => c.Trim().Trim('*', '`').ToLowerInvariant()).ToList();
        var rangeColumn = header.FindIndex(h => h is "sub-block" or "subblock" or "range");
        var packageColumn = header.FindIndex(h => h is "package" or "owner");
        if (rangeColumn < 0 || packageColumn < 0)
            yield break;

        foreach (var row in table.Skip(2))
        {
            var cells = Cells(row);
            if (cells.Count <= Math.Max(rangeColumn, packageColumn))
                continue;

            if (!TryReadRange(cells[rangeColumn], domain.EventIdBlockStart, out var start, out var end))
                continue;

            var (primary, adapters, adapterOffset) = ReadPackages(cells[packageColumn], assemblies);
            if (adapters.Count > 0 && adapterOffset is { } offset && offset > 0)
            {
                if (primary.Count > 0)
                    yield return new EventIdOwnershipEntry(primary, start, start + offset - 1, domain.Id);
                yield return new EventIdOwnershipEntry(adapters, start + offset, end, domain.Id);
            }
            else if (primary.Count > 0)
            {
                yield return new EventIdOwnershipEntry(primary, start, end, domain.Id);
            }
        }
    }

    private static List<string> Cells(string row)
    {
        var trimmed = row.Trim();
        if (trimmed.StartsWith('|'))
            trimmed = trimmed[1..];
        if (trimmed.EndsWith('|'))
            trimmed = trimmed[..^1];
        return [.. trimmed.Split('|').Select(c => c.Trim())];
    }

    private static bool TryReadRange(string cell, int domainBlockStart, out int start, out int end)
    {
        cell = cell.Replace("`", string.Empty).Replace("*", string.Empty);
        var relative = RelativeRange().Match(cell);
        if (relative.Success)
        {
            start = domainBlockStart + int.Parse(relative.Groups[1].Value, CultureInfo.InvariantCulture);
            end = domainBlockStart + int.Parse(relative.Groups[2].Value, CultureInfo.InvariantCulture);
            return end >= start;
        }

        var absolute = AbsoluteRange().Match(cell);
        if (absolute.Success)
        {
            start = int.Parse(absolute.Groups[1].Value, CultureInfo.InvariantCulture);
            end = int.Parse(absolute.Groups[2].Value, CultureInfo.InvariantCulture);
            return end >= start;
        }

        start = end = 0;
        return false;
    }

    private static (List<string> Primary, List<string> Adapters, int? AdapterOffset) ReadPackages(string cell, List<string> assemblies)
    {
        var adapters = new List<string>();
        int? adapterOffset = null;

        var withoutParenthetical = Parenthetical().Replace(cell, " ");
        foreach (Match parenthetical in Parenthetical().Matches(cell))
        {
            var inner = parenthetical.Groups[1].Value;
            var offset = AdapterOffset().Match(inner);
            if (!offset.Success)
                continue;

            adapterOffset = int.Parse(offset.Groups[1].Value, CultureInfo.InvariantCulture);
            foreach (Match word in DottedWord().Matches(inner))
            {
                if (Resolve(word.Value, assemblies) is { } adapter)
                    adapters.Add(adapter);
            }
        }

        var primary = new List<string>();
        foreach (var word in WordSplit().Split(withoutParenthetical.Replace("`", " ")))
        {
            if (word.Length == 0)
                continue;
            if (Resolve(word, assemblies) is { } resolved && !primary.Contains(resolved))
                primary.Add(resolved);
        }

        return (primary, adapters, adapterOffset);
    }

    private static string? Resolve(string word, List<string> assemblies)
    {
        var token = word.Trim('`', ' ', ',', ';', '(', ')', '*', '.');
        if (token.Length == 0 || !char.IsLetter(token[0]))
            return null;

        var exact = assemblies.FirstOrDefault(a =>
            a.Equals(token, StringComparison.OrdinalIgnoreCase) || a.Equals("SharedKernel." + token, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
            return exact;

        var suffix = assemblies
            .Where(a => a.EndsWith("." + token, StringComparison.OrdinalIgnoreCase))
            .OrderBy(a => a.Count(c => c == '.'))
            .ToList();
        if (suffix.Count == 0)
            return null;

        // The shortest name wins only when it is the unique shortest.
        return suffix.Count == 1 || suffix[0].Count(c => c == '.') < suffix[1].Count(c => c == '.') ? suffix[0] : null;
    }

    [GeneratedRegex(@"\+\s*(\d+)\s*[–—-]\s*\+\s*(\d+)")]
    private static partial Regex RelativeRange();

    [GeneratedRegex(@"(\d{4,5})\s*[–—-]\s*(\d{4,5})")]
    private static partial Regex AbsoluteRange();

    [GeneratedRegex(@"\(([^)]*)\)")]
    private static partial Regex Parenthetical();

    [GeneratedRegex(@"\bat\s+(\d+)\s*[–—-]\s*\d+")]
    private static partial Regex AdapterOffset();

    [GeneratedRegex(@"\.[A-Za-z][\w.]*")]
    private static partial Regex DottedWord();

    [GeneratedRegex(@"[\s,+/]+|\band\b")]
    private static partial Regex WordSplit();
}

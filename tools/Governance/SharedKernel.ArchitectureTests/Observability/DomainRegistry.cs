using System.Text.RegularExpressions;

namespace SharedKernel.ArchitectureTests.Observability;

/// <summary>
/// One capability domain from the root <c>CLAUDE.md</c> table.
/// </summary>
/// <param name="Id">The domain id, for example <c>06.Persistence</c>.</param>
/// <param name="Number">The domain number, which is also its EventId block (<c>Number * 1000</c> to <c>+999</c>).</param>
/// <param name="Slug">The agent slug, for example <c>persistence</c>.</param>
/// <param name="Folder">The repository-relative folder with forward slashes and no trailing slash, for example <c>src/Infrastructure/Persistence</c>.</param>
public sealed record DomainInfo(string Id, int Number, string Slug, string Folder)
{
    /// <summary>Gets the first EventId of the domain's block.</summary>
    public int EventIdBlockStart => Number * 1000;

    /// <summary>Gets the last EventId of the domain's block.</summary>
    public int EventIdBlockEnd => EventIdBlockStart + 999;
}

/// <summary>
/// The domain registry, parsed from the domain table of the root <c>CLAUDE.md</c>: the one place that maps a folder to a domain.
/// </summary>
/// <remarks>
/// Nothing here lists a domain; adding a row to the root table is the whole change. The domain of a project is the longest
/// folder prefix of its path, so a package living in a capability folder belongs to that capability's domain.
/// </remarks>
public sealed partial class DomainRegistry
{
    /// <summary>The id given to a path that is under no registered folder.</summary>
    public const string UnknownDomain = "unknown";

    private readonly List<DomainInfo> _domains;

    private DomainRegistry(List<DomainInfo> domains) => _domains = domains;

    /// <summary>Gets the registered domains, ordered by number.</summary>
    public IReadOnlyList<DomainInfo> Domains => _domains;

    /// <summary>Parses the domain table of the root <c>CLAUDE.md</c>.</summary>
    /// <param name="rootClaudeMarkdown">The full text of the root <c>CLAUDE.md</c>.</param>
    /// <returns>The registry.</returns>
    /// <exception cref="InvalidOperationException">No domain row was found.</exception>
    public static DomainRegistry Parse(string rootClaudeMarkdown)
    {
        ArgumentNullException.ThrowIfNull(rootClaudeMarkdown);

        var domains = new List<DomainInfo>();
        foreach (var line in rootClaudeMarkdown.Split('\n'))
        {
            var match = DomainRow().Match(line);
            if (!match.Success)
                continue;

            var id = match.Groups["id"].Value;
            domains.Add(new DomainInfo(
                id,
                int.Parse(id[..2], System.Globalization.CultureInfo.InvariantCulture),
                match.Groups["slug"].Value,
                Normalize(match.Groups["folder"].Value)));
        }

        return domains.Count == 0
            ? throw new InvalidOperationException("No domain row (`NN.Name` | `slug` | [`folder`](…)) found in the root CLAUDE.md.")
            : new DomainRegistry([.. domains.OrderBy(d => d.Number)]);
    }

    /// <summary>Finds the domain of a repository-relative path: the longest registered folder prefix.</summary>
    /// <param name="repositoryRelativePath">A path relative to the repository root, with either separator.</param>
    /// <returns>The domain, or <see langword="null"/> when the path is under no registered folder.</returns>
    public DomainInfo? Find(string repositoryRelativePath)
    {
        ArgumentNullException.ThrowIfNull(repositoryRelativePath);

        var path = Normalize(repositoryRelativePath) + "/";
        DomainInfo? best = null;
        foreach (var domain in _domains)
        {
            if (path.StartsWith(domain.Folder + "/", StringComparison.OrdinalIgnoreCase) && (best is null || domain.Folder.Length > best.Folder.Length))
                best = domain;
        }

        return best;
    }

    /// <summary>Gets the domain id of a path, or <see cref="UnknownDomain"/>.</summary>
    /// <param name="repositoryRelativePath">A path relative to the repository root.</param>
    /// <returns>The domain id.</returns>
    public string IdOf(string repositoryRelativePath) => Find(repositoryRelativePath)?.Id ?? UnknownDomain;

    private static string Normalize(string path) => path.Replace('\\', '/').Trim().TrimEnd('/');

    [GeneratedRegex(@"^\|\s*`(?<id>\d{2}\.\w+)`\s*\|\s*`(?<slug>\w+)`\s*\|\s*\[`(?<folder>[^`]+)`\]", RegexOptions.CultureInvariant)]
    private static partial Regex DomainRow();
}

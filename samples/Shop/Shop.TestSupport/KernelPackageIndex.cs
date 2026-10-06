using System.Text.RegularExpressions;

namespace Shop.TestSupport;

/// <summary>
/// Every packable SharedKernel package and its tier, read from the repository's generated <c>docs/packages.md</c> —
/// the same index CI keeps in step with the code, so no Shop test keeps its own copy of the tiers.
/// </summary>
public sealed partial class KernelPackageIndex
{
    private KernelPackageIndex(IReadOnlyDictionary<string, string> tiers) => Tiers = tiers;

    /// <summary>Package id to tier (<c>Foundation</c>, <c>Model</c>, <c>Abstractions</c>, <c>Adapter</c>, <c>Host</c>, <c>Testing</c>, <c>Tooling</c>).</summary>
    public IReadOnlyDictionary<string, string> Tiers { get; }

    /// <summary>The index, loaded once.</summary>
    public static KernelPackageIndex Instance => LazyInstance.Value;

    /// <summary>The repository root: the first folder above the test binaries that holds <c>docs/packages.md</c>.</summary>
    public static string RepositoryRoot => LazyRoot.Value;

    private static readonly Lazy<string> LazyRoot = new(FindRepositoryRoot);
    private static readonly Lazy<KernelPackageIndex> LazyInstance = new(Load);

    /// <summary>The tier of a kernel package, or <c>ThirdParty</c> for anything else.</summary>
    public string TierOf(string package) =>
        Tiers.TryGetValue(package, out var tier) ? tier : "ThirdParty";

    private static KernelPackageIndex Load()
    {
        var tiers = new Dictionary<string, string>(StringComparer.Ordinal);
        string? tier = null;
        foreach (string line in File.ReadLines(Path.Combine(RepositoryRoot, "docs", "packages.md")))
        {
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                tier = line[3..].Trim();
                continue;
            }

            var package = PackageRow().Match(line);
            if (tier is not null && package.Success)
            {
                tiers[package.Groups["id"].Value] = tier;
            }
        }

        return tiers.Count > 0
            ? new KernelPackageIndex(tiers)
            : throw new InvalidOperationException("docs/packages.md lists no packages.");
    }

    private static string FindRepositoryRoot()
    {
        for (
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            dir is not null;
            dir = dir.Parent
        )
        {
            if (File.Exists(Path.Combine(dir.FullName, "docs", "packages.md")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException(
            $"No docs/packages.md above {AppContext.BaseDirectory}; Shop tests run from inside the repository."
        );
    }

    [GeneratedRegex(@"^\| \[`(?<id>SharedKernel\.[A-Za-z0-9.]+)`\]")]
    private static partial Regex PackageRow();
}

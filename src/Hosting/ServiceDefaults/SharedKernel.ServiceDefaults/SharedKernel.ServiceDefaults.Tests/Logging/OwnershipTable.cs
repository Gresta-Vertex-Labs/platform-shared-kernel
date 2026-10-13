using System.Text.RegularExpressions;

namespace SharedKernel.ServiceDefaults.Tests.Logging;

/// <summary>
/// Reads the package to sub-block index table of the domain <c>CLAUDE.md</c> (<c>## Logging</c>): rows of the form
/// <c>| 2 | 13200-13299 | `SharedKernel.ServiceDefaults.Security` | … |</c>. Only the packages that log are returned.
/// </summary>
internal static partial class OwnershipTable
{
    /// <summary>Package name to sub-block index, for every row whose sub-block holds a message today.</summary>
    public static IReadOnlyDictionary<string, int> Read()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Platform.SharedKernel.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        var claude = Path.Combine(directory!.FullName, "src", "Hosting", "ServiceDefaults", "CLAUDE.md");
        var table = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var line in File.ReadAllLines(claude))
        {
            var match = Row().Match(line);
            if (match.Success && !line.Contains("reserved", StringComparison.OrdinalIgnoreCase))
            {
                table[match.Groups["package"].Value] = int.Parse(match.Groups["index"].Value, System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        return table;
    }

    [GeneratedRegex(@"^\|\s*(?<index>\d)\s*\|\s*13\d00-13\d99\s*\|\s*`(?<package>SharedKernel[\w.]*)`\s*\|")]
    private static partial Regex Row();
}

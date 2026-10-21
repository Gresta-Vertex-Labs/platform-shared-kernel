namespace SharedKernel.ArchitectureTests.ServiceShape;

/// <summary>
/// The tier of every packable SharedKernel package, read from a table embedded in this assembly. The table is
/// generated from each package's declared tier when this package is built, so it is exactly as current as the package
/// version you reference: nothing is read from the repository, and no test keeps its own copy of the tiers.
/// </summary>
public sealed class KernelPackageIndex
{
    /// <summary>What a package outside the kernel reports as its tier.</summary>
    public const string ThirdParty = "ThirdParty";

    private const string KernelPrefix = "SharedKernel.";
    private const string ResourceName = "kernel-package-tiers.tsv";

    private static readonly string[] KnownTiers =
    [
        KernelTier.Foundation,
        KernelTier.Model,
        KernelTier.Abstractions,
        KernelTier.Adapter,
        KernelTier.Host,
        KernelTier.Testing,
        KernelTier.Tooling,
    ];

    private static readonly Lazy<KernelPackageIndex> LazyInstance = new(LoadEmbedded);

    private KernelPackageIndex(IReadOnlyDictionary<string, string> tiers) => Tiers = tiers;

    /// <summary>The index of the kernel version this package shipped with, loaded once.</summary>
    /// <exception cref="InvalidOperationException">The embedded table is missing, empty or malformed.</exception>
    public static KernelPackageIndex Instance => LazyInstance.Value;

    /// <summary>Package id to tier, for every packable kernel package (Tooling included).</summary>
    public IReadOnlyDictionary<string, string> Tiers { get; }

    /// <summary>Whether <paramref name="package"/> is named like a SharedKernel package (<c>SharedKernel.*</c>).</summary>
    /// <param name="package">The package id.</param>
    /// <returns><see langword="true"/> for a name that starts with <c>SharedKernel.</c>.</returns>
    public static bool IsKernelPackage(string package) =>
        package.StartsWith(KernelPrefix, StringComparison.Ordinal);

    /// <summary>The tier of <paramref name="package"/>, or <see cref="ThirdParty"/> for anything the table does not list.</summary>
    /// <param name="package">The package id.</param>
    /// <returns>One of the <see cref="KernelTier"/> names, or <see cref="ThirdParty"/>.</returns>
    public string TierOf(string package) => Tiers.TryGetValue(package, out string? tier) ? tier : ThirdParty;

    /// <summary>
    /// Parses a table: a <c>#</c> comment line, then one <c>{PackageId}&lt;TAB&gt;{Tier}</c> line per package.
    /// </summary>
    internal static KernelPackageIndex Parse(IEnumerable<string> lines)
    {
        var tiers = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string line in lines)
        {
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            string[] fields = line.Split('\t');
            if (fields.Length != 2 || fields[0].Length == 0 || !KnownTiers.Contains(fields[1], StringComparer.Ordinal))
            {
                throw new InvalidOperationException(
                    $"The kernel package index has a malformed line '{line}'; expected the package id, a tab and a known tier."
                );
            }

            if (!tiers.TryAdd(fields[0], fields[1]))
            {
                throw new InvalidOperationException($"The kernel package index lists '{fields[0]}' twice.");
            }
        }

        return tiers.Count > 0
            ? new KernelPackageIndex(tiers)
            : throw new InvalidOperationException("The kernel package index lists no packages.");
    }

    private static KernelPackageIndex LoadEmbedded()
    {
        using Stream stream =
            typeof(KernelPackageIndex).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"The embedded resource '{ResourceName}' is missing from {typeof(KernelPackageIndex).Assembly.GetName().Name}."
            );
        using var reader = new StreamReader(stream);
        var lines = new List<string>();
        while (reader.ReadLine() is { } line)
        {
            lines.Add(line);
        }

        return Parse(lines);
    }
}

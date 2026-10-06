using System.Xml.Linq;
using FluentAssertions;
using Shop.TestSupport;
using Xunit;
using Xunit.Abstractions;

namespace Shop.Coverage.Tests;

/// <summary>
/// The Shop exists to prove every kernel package works in a real system, so every packable package must be referenced
/// directly by at least one Shop project (a transitive reference proves nothing about the package's own API).
/// </summary>
public sealed class PackageCoverageTests(ITestOutputHelper output)
{
    private static readonly string ShopRoot = Path.Combine(
        KernelPackageIndex.RepositoryRoot,
        "samples",
        "Shop"
    );

    [Fact]
    public void Report_WhichPackagesTheShopReferences()
    {
        var referenced = ReferencedByShop();
        var missing = Missing(referenced);

        output.WriteLine(
            $"{KernelPackageIndex.Instance.Tiers.Count - missing.Count} of {KernelPackageIndex.Instance.Tiers.Count} packages referenced directly."
        );
        foreach (
            var group in missing
                .GroupBy(KernelPackageIndex.Instance.TierOf)
                .OrderBy(g => g.Key, StringComparer.Ordinal)
        )
        {
            output.WriteLine($"Not yet referenced ({group.Key}): {string.Join(", ", group)}");
        }

        referenced.Should().NotBeEmpty();
    }

    [Fact(Skip = "Strict once every Shop service exists (the last PR of the Shop switches it on).")]
    public void EveryPackage_IsReferencedDirectly_BySomeShopProject() =>
        Missing(ReferencedByShop())
            .Should()
            .BeEmpty("every kernel package needs a home in the Shop");

    private static List<string> Missing(IReadOnlySet<string> referenced) =>
        KernelPackageIndex
            .Instance.Tiers.Keys.Where(package => !referenced.Contains(package))
            .OrderBy(package => package, StringComparer.Ordinal)
            .ToList();

    private static HashSet<string> ReferencedByShop() =>
        Directory
            .EnumerateFiles(ShopRoot, "*.*", SearchOption.AllDirectories)
            .Where(path =>
                path.EndsWith(".csproj", StringComparison.Ordinal)
                || path.EndsWith(".props", StringComparison.Ordinal)
            )
            .Where(path =>
                !path.Contains(
                    $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                    StringComparison.Ordinal
                )
            )
            .SelectMany(path => XDocument.Load(path).Descendants("PackageReference"))
            .Select(reference => (string?)reference.Attribute("Include"))
            .OfType<string>()
            .Where(id => id.StartsWith("SharedKernel.", StringComparison.Ordinal))
            .ToHashSet(StringComparer.Ordinal);
}

using System.Text.RegularExpressions;
using System.Xml.Linq;
using FluentAssertions;
using Xunit;

namespace SharedKernel.ArchitectureTests.Tests;

/// <summary>
/// Locks the mechanical parts of the package README standard (<c>docs/package-readme-standard.md</c>) for every packable
/// production project: the README ships inside the NuGet package, so its title, required sections, tier badge and links
/// must be right on the package feed as well as on GitHub.
/// </summary>
/// <remarks>
/// A packable production project is one that declares <c>&lt;SharedKernelTier&gt;</c> and does not set
/// <c>&lt;IsPackable&gt;false&lt;/IsPackable&gt;</c>; test projects, consumer-verify harnesses and samples declare no tier.
/// </remarks>
public sealed partial class PackageReadmeStandardTests
{
    // Required headings, in the order the standard fixes. Optional sections may sit between them.
    private static readonly string[] RequiredSections = ["Install", "Quick start", "Reference", "Testing", "Pitfalls"];

    // Every heading the standard names, in order; any other `##` heading is rejected so the shape stays uniform.
    private static readonly string[] KnownSections =
    [
        "Contents", "Install", "Quick start", "How it works", "Recipes", "Configuration", "Reference", "Testing",
        "Pitfalls", "Design decisions",
    ];

    // The standard asks for about 500 lines; the hard limit leaves room for a growing package before it must be split.
    private const int MaxPackageReadmeLines = 550;

    [Fact]
    public void PackableProjects_AreFound()
    {
        var projects = PackableProjects().ToList();

        projects.Should().Contain(p => p.PackageId == "SharedKernel.Primitives", "the scan must see the repository's packages");
        projects.Should().HaveCountGreaterThan(90, "every packable SharedKernel package is covered");
    }

    [Theory]
    [MemberData(nameof(Packages))]
    public void Readme_FollowsThePackageReadmeStandard(string packageId)
    {
        var project = PackableProjects().Single(p => p.PackageId == packageId);
        var readmePath = Path.Combine(Path.GetDirectoryName(project.Path)!, "README.md");
        File.Exists(readmePath).Should().BeTrue($"{packageId} ships a README.md (SKPKG003)");

        var markdown = StripCodeBlocks(File.ReadAllText(readmePath));
        var problems = new List<string>();

        var title = TitleLine().Match(markdown);
        if (!title.Success || title.Groups[1].Value.Trim() != packageId)
            problems.Add($"the first heading must be '# {packageId}'");

        if (!markdown.Contains($"tier-{project.Tier}", StringComparison.Ordinal))
            problems.Add($"the Tier badge must say {project.Tier} (tier-{project.Tier})");

        var sections = SectionHeading().Matches(markdown).Select(m => m.Groups[1].Value.Trim()).ToList();
        foreach (var unknown in sections.Where(s => !KnownSections.Contains(s)))
            problems.Add($"'## {unknown}' is not a standard section");

        foreach (var required in RequiredSections.Where(r => !sections.Contains(r)))
            problems.Add($"the required section '## {required}' is missing");

        var order = sections.Where(KnownSections.Contains).Select(s => Array.IndexOf(KnownSections, s)).ToList();
        if (!order.SequenceEqual(order.Order()))
            problems.Add($"sections are out of order: {string.Join(" → ", sections)}");

        foreach (Match link in MarkdownLink().Matches(markdown))
        {
            var target = link.Groups[1].Value.Trim();
            if (!target.StartsWith("https://", StringComparison.Ordinal) && !target.StartsWith("http://", StringComparison.Ordinal)
                && !target.StartsWith('#') && !target.StartsWith("mailto:", StringComparison.Ordinal))
                problems.Add($"relative link '{target}' breaks on the package feed; use an absolute GitHub URL");
            else if (MaintainerDocument().IsMatch(target))
                problems.Add($"link '{target}' points at maintainer material (CLAUDE.md, state-map.md or docs/)");
        }

        if (!markdown.Contains("Part of [Platform.SharedKernel]", StringComparison.Ordinal))
            problems.Add("the footer 'Part of [Platform.SharedKernel](…)' is missing");

        var lines = File.ReadAllLines(readmePath).Length;
        if (lines > MaxPackageReadmeLines)
            problems.Add($"the README is {lines} lines; keep it under {MaxPackageReadmeLines} and move walkthroughs to the domain README or samples/");

        problems.Should().BeEmpty($"{Path.GetRelativePath(FindRepositoryRoot().FullName, readmePath)} follows docs/package-readme-standard.md");
    }

    [Theory]
    [MemberData(nameof(PublicReadmes))]
    public void PublicReadme_NamesCapabilities_NotDomainIds(string relativePath)
    {
        var markdown = StripCodeBlocks(File.ReadAllText(Path.Combine(FindRepositoryRoot().FullName, relativePath)));

        var ids = DomainId().Matches(markdown).Select(m => m.Value).Distinct().ToList();

        ids.Should().BeEmpty($"{relativePath} is read on GitHub and the package feed, where a capability is named by its folder (Persistence), not its domain id (06.Persistence)");
    }

    [Theory]
    [MemberData(nameof(DomainReadmes))]
    public void DomainReadme_PackagesBadge_MatchesThePackagesItOwns(string relativePath, int expected)
    {
        var markdown = File.ReadAllText(Path.Combine(FindRepositoryRoot().FullName, relativePath));

        var badge = PackagesBadge().Match(markdown);

        badge.Success.Should().BeTrue($"{relativePath} shows a packages-N badge");
        int.Parse(badge.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)
            .Should().Be(expected, $"{relativePath} counts the packable packages it owns (capability fakes count under src/Testing)");
    }

    /// <summary>One row per packable production project.</summary>
    /// <returns>The package ids.</returns>
    public static IEnumerable<object[]> Packages() => PackableProjects().Select(p => new object[] { p.PackageId });

    /// <summary>Every README a GitHub or package-feed reader sees: packages, domains, samples and the root.</summary>
    /// <returns>Repository-relative README paths.</returns>
    public static IEnumerable<object[]> PublicReadmes()
    {
        var root = FindRepositoryRoot().FullName;
        return Directory.EnumerateFiles(root, "README.md", SearchOption.AllDirectories)
            .Where(p => !IgnoredPath().IsMatch(p) && !MaintainerTree().IsMatch(p))
            .Select(p => Path.GetRelativePath(root, p).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .Select(p => new object[] { p });
    }

    /// <summary>
    /// Every domain README (a folder that also holds a <c>state-map.md</c>) with the package count its badge must show:
    /// the root counts every package, <c>src/Testing</c> every Testing-tier package, and a capability folder its own
    /// packages except the <c>.Testing</c> fakes kept beside them.
    /// </summary>
    /// <returns>Repository-relative README path and expected count.</returns>
    public static IEnumerable<object[]> DomainReadmes()
    {
        var root = FindRepositoryRoot().FullName;
        var projects = PackableProjects().ToList();

        foreach (var stateMap in Directory.EnumerateFiles(root, "state-map.md", SearchOption.AllDirectories)
                     .Where(p => !IgnoredPath().IsMatch(p))
                     .Order(StringComparer.Ordinal))
        {
            var folder = Path.GetDirectoryName(stateMap)!;
            if (!File.Exists(Path.Combine(folder, "README.md")))
                continue;

            var relative = Path.GetRelativePath(root, folder).Replace('\\', '/');
            var inFolder = projects.Where(p => p.Path.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
            var expected = relative switch
            {
                "." => projects.Count,
                "src/Testing" => projects.Count(p => p.Tier == "Testing"),
                _ => inFolder.Count(p => p.Tier != "Testing"),
            };

            yield return [relative == "." ? "README.md" : $"{relative}/README.md", expected];
        }
    }

    private static IEnumerable<(string Path, string PackageId, string Tier)> PackableProjects()
    {
        var root = FindRepositoryRoot();
        foreach (var path in Directory.EnumerateFiles(root.FullName, "*.csproj", SearchOption.AllDirectories)
                     .Where(p => !IgnoredPath().IsMatch(p))
                     .Order(StringComparer.Ordinal))
        {
            var document = XDocument.Load(path);
            var tier = Property(document, "SharedKernelTier");
            if (tier is null || string.Equals(Property(document, "IsPackable"), "false", StringComparison.OrdinalIgnoreCase))
                continue;

            yield return (path, Property(document, "PackageId") ?? Path.GetFileNameWithoutExtension(path), tier);
        }
    }

    private static string? Property(XDocument document, string name) =>
        document.Descendants().FirstOrDefault(e => e.Name.LocalName == name)?.Value.Trim();

    // Fenced code may legitimately contain '#' comments and link-like text; only prose counts.
    private static string StripCodeBlocks(string markdown) => CodeFence().Replace(markdown, string.Empty);

    private static DirectoryInfo FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Platform.SharedKernel.slnx")))
            directory = directory.Parent;

        return directory ?? throw new InvalidOperationException("Platform.SharedKernel.slnx not found above the test output directory.");
    }

    [GeneratedRegex(@"[\\/](bin|obj|nupkgs|\.git|node_modules)[\\/]", RegexOptions.IgnoreCase)]
    private static partial Regex IgnoredPath();

    [GeneratedRegex(@"^(`{3,}|~{3,}).*?^\1\s*$", RegexOptions.Multiline | RegexOptions.Singleline)]
    private static partial Regex CodeFence();

    [GeneratedRegex(@"\A\s*#\s+(.+?)\s*$", RegexOptions.Multiline)]
    private static partial Regex TitleLine();

    [GeneratedRegex(@"^##\s+(.+?)\s*$", RegexOptions.Multiline)]
    private static partial Regex SectionHeading();

    [GeneratedRegex(@"\]\(\s*<?([^)\s>]+)>?(?:\s+""[^""]*"")?\s*\)")]
    private static partial Regex MarkdownLink();

    [GeneratedRegex(@"(CLAUDE\.md|state-map\.md|/blob/[^/]+/docs/)", RegexOptions.IgnoreCase)]
    private static partial Regex MaintainerDocument();

    // Agent and generated trees are not read on GitHub as documentation.
    [GeneratedRegex(@"[\\/](\.claude|\.github|graphify-out|archive|artifacts)[\\/]", RegexOptions.IgnoreCase)]
    private static partial Regex MaintainerTree();

    // A domain id such as 06.Persistence or 16.Testing.
    [GeneratedRegex(@"(?<![\w.])\d{2}\.[A-Z][A-Za-z]+")]
    private static partial Regex DomainId();

    [GeneratedRegex(@"badge/packages-(\d+)-")]
    private static partial Regex PackagesBadge();
}

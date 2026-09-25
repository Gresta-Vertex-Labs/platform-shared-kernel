using System.Text.RegularExpressions;
using System.Xml.Linq;
using FluentAssertions;
using Xunit;

namespace SharedKernel.ArchitectureTests.Tests;

/// <summary>
/// Repository-wide view of the package tiers (WO-086 / P-563). The build enforces the same matrix per project in
/// <c>eng/SharedKernelTiers.targets</c>; these tests check what a single project build cannot see: every packable project
/// declares a tier, the whole reference graph has no cycles, and <c>eng/tier-baseline.txt</c> lists only edges that still exist.
/// </summary>
public sealed partial class DependencyGraphRulesTests
{
    private static readonly Dictionary<string, string[]> AllowedTiers = new(StringComparer.Ordinal)
    {
        ["Foundation"] = ["Foundation"],
        ["Model"] = ["Foundation", "Model"],
        ["Abstractions"] = ["Foundation", "Model", "Abstractions"],
        ["Adapter"] = ["Foundation", "Model", "Abstractions"],
        ["Host"] = ["Foundation", "Model", "Abstractions", "Adapter", "Host"],
        ["Testing"] = ["Foundation", "Model", "Abstractions", "Adapter", "Host", "Testing"],
        ["Tooling"] = [],
    };

    private static readonly Lazy<RepositoryGraph> Graph = new(RepositoryGraph.Load);

    [Fact]
    public void EveryPackableProject_DeclaresAKnownTier()
    {
        var graph = Graph.Value;
        graph.Projects.Should().Contain(p => p.Name == "SharedKernel.Primitives", "the repository's project files must be found");

        var missing = graph.Projects
            .Where(p => p.IsProduction && p.Tier is null)
            .Select(p => p.RelativePath);
        var unknown = graph.Projects
            .Where(p => p.Tier is not null && !AllowedTiers.ContainsKey(p.Tier))
            .Select(p => $"{p.RelativePath}: '{p.Tier}'");

        missing.Should().BeEmpty("every packable library declares <SharedKernelTier> (SKTIER005)");
        unknown.Should().BeEmpty("tiers are Foundation, Model, Abstractions, Adapter, Host, Testing or Tooling");
    }

    [Fact]
    public void EveryDirectReference_RespectsTheTierMatrix_OrIsBaselined()
    {
        var graph = Graph.Value;
        var violations = graph.Violations().Where(v => !graph.Baseline.Contains(v.Key)).Select(v => v.Description);

        violations.Should().BeEmpty("a tier may reference only the tiers listed in eng/SharedKernelTiers.targets");
    }

    [Fact]
    public void Baseline_ListsOnlyEdgesThatStillBreakTheMatrix()
    {
        var graph = Graph.Value;
        var live = graph.Violations().Select(v => v.Key).ToHashSet(StringComparer.Ordinal);

        graph.Baseline.Where(entry => !live.Contains(entry)).Should().BeEmpty(
            "a fixed edge must be removed from eng/tier-baseline.txt in the same change, so the baseline only ever shrinks");
    }

    [Fact]
    public void TestingPackages_AreReferencedOnlyByTestingProjectsOrTests()
    {
        var graph = Graph.Value;
        var offenders = graph.Projects
            .Where(p => p.Tier is not null && p.Tier != "Testing")
            .SelectMany(p => p.ProjectReferences
                .Select(graph.Find)
                .Where(r => r?.Tier == "Testing")
                .Select(r => $"{p.Name} -> {r!.Name}"));

        offenders.Should().BeEmpty("test-helper packages are referenced by test projects only");
    }

    /// <summary>
    /// WO-086 / P-567: the kernel owns the mediator contracts (<c>SharedKernel.Application</c>), and MediatR is an
    /// implementation detail of one adapter. Any other shipped project taking a MediatR reference would put MediatR
    /// types back into a public contract, which is what the abstraction exists to prevent.
    /// </summary>
    [Fact]
    public void MediatR_IsReferencedOnlyByTheMediatorAdapter()
    {
        const string adapter = "SharedKernel.Application.Mediator.MediatR";
        var graph = Graph.Value;
        graph.Projects.Should().Contain(p => p.Name == adapter && p.RuntimePackages.Contains("MediatR"), "the adapter is the one project that references MediatR");

        var offenders = graph.Projects
            .Where(p => p.Tier is not null && p.Name != adapter)
            .Where(p => p.RuntimePackages.Any(static package => package is "MediatR" or "MediatR.Contracts"))
            .Select(p => p.RelativePath);

        offenders.Should().BeEmpty($"MediatR is referenced only by {adapter}; everything else depends on SharedKernel.Application's ISender");
    }

    [Fact]
    public void ProjectReferenceGraph_HasNoCycles()
    {
        var graph = Graph.Value;
        var state = new Dictionary<string, int>(StringComparer.Ordinal);
        var cycles = new List<string>();

        foreach (var project in graph.Projects)
            Visit(project, [], graph, state, cycles);

        cycles.Should().BeEmpty();
    }

    private static void Visit(ProjectNode node, List<string> path, RepositoryGraph graph, Dictionary<string, int> state, List<string> cycles)
    {
        if (state.TryGetValue(node.Name, out var s))
        {
            if (s == 1)
                cycles.Add(string.Join(" -> ", path.SkipWhile(p => p != node.Name).Append(node.Name)));
            return;
        }

        state[node.Name] = 1;
        path.Add(node.Name);
        foreach (var reference in node.ProjectReferences.Select(graph.Find).OfType<ProjectNode>())
            Visit(reference, path, graph, state, cycles);
        path.RemoveAt(path.Count - 1);
        state[node.Name] = 2;
    }

    private sealed record ProjectNode(
        string Name,
        string RelativePath,
        string? Tier,
        bool IsProduction,
        IReadOnlyList<string> ProjectReferences,
        IReadOnlyList<string> AllowedAdapters,
        IReadOnlyList<string> RuntimePackages);

    private sealed record Violation(string Key, string Description);

    private sealed partial class RepositoryGraph
    {
        private readonly Dictionary<string, ProjectNode> _byName;

        private RepositoryGraph(IReadOnlyList<ProjectNode> projects, IReadOnlySet<string> baseline)
        {
            Projects = projects;
            Baseline = baseline;
            _byName = projects.GroupBy(p => p.Name, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        }

        public IReadOnlyList<ProjectNode> Projects { get; }

        public IReadOnlySet<string> Baseline { get; }

        public ProjectNode? Find(string name) => _byName.GetValueOrDefault(name);

        public IEnumerable<Violation> Violations()
        {
            var allowList = AbstractionsPackageAllowList();
            foreach (var project in Projects.Where(p => p.Tier is not null && AllowedTiers.ContainsKey(p.Tier)))
            {
                foreach (var reference in project.ProjectReferences.Select(Find).OfType<ProjectNode>())
                {
                    if (reference.Tier is null || AllowedTiers[project.Tier!].Contains(reference.Tier)
                        || project.AllowedAdapters.Contains(reference.Name))
                        continue;

                    yield return new Violation(
                        $"{project.Name}->{reference.Name}",
                        $"{project.Name} ({project.Tier}) -> {reference.Name} ({reference.Tier})");
                }

                if (project.Tier is "Model" or "Abstractions")
                {
                    foreach (var package in project.RuntimePackages.Where(p => !allowList.IsMatch(p)))
                        yield return new Violation($"{project.Name}->package:{package}", $"{project.Name} ({project.Tier}) -> NuGet {package}");
                }
            }
        }

        public static RepositoryGraph Load()
        {
            var root = FindRepositoryRoot();
            var projects = Directory.EnumerateFiles(root.FullName, "*.csproj", SearchOption.AllDirectories)
                .Where(p => !IgnoredPath().IsMatch(p))
                .Select(p => Parse(root, p))
                .ToList();

            var baselineFile = Path.Combine(root.FullName, "eng", "tier-baseline.txt");
            var baseline = File.Exists(baselineFile)
                ? File.ReadAllLines(baselineFile).Select(l => l.Replace(" ", string.Empty, StringComparison.Ordinal))
                    .Where(l => l.Length > 0 && !l.StartsWith('#')).ToHashSet(StringComparer.Ordinal)
                : new HashSet<string>(StringComparer.Ordinal);

            return new RepositoryGraph(projects, baseline);
        }

        private static ProjectNode Parse(DirectoryInfo root, string path)
        {
            var document = XDocument.Load(path);
            var name = Path.GetFileNameWithoutExtension(path);
            string? Property(string property) =>
                document.Descendants().FirstOrDefault(e => e.Name.LocalName == property)?.Value.Trim() is { Length: > 0 } v ? v : null;

            var isTest = name.EndsWith(".Tests", StringComparison.Ordinal);
            var isExe = string.Equals(Property("OutputType"), "Exe", StringComparison.OrdinalIgnoreCase);
            var isProduction = !isTest && !isExe && !string.Equals(Property("IsPackable"), "false", StringComparison.OrdinalIgnoreCase);

            var references = document.Descendants().Where(e => e.Name.LocalName == "ProjectReference")
                .Select(e => Path.GetFileNameWithoutExtension((e.Attribute("Include")?.Value ?? string.Empty).Replace('\\', '/')))
                .Where(n => n.Length > 0)
                .ToList();
            var packages = document.Descendants().Where(e => e.Name.LocalName == "PackageReference")
                .Where(e => !string.Equals(e.Attribute("PrivateAssets")?.Value ?? e.Elements().FirstOrDefault(c => c.Name.LocalName == "PrivateAssets")?.Value, "all", StringComparison.OrdinalIgnoreCase))
                .Select(e => e.Attribute("Include")?.Value ?? string.Empty)
                .Where(n => n.Length > 0)
                .ToList();
            var adapters = (Property("SharedKernelAllowedAdapterReferences") ?? string.Empty)
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            return new ProjectNode(name, Path.GetRelativePath(root.FullName, path), Property("SharedKernelTier"), isProduction, references, adapters, packages);
        }

        private static DirectoryInfo FindRepositoryRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Platform.SharedKernel.slnx")))
                directory = directory.Parent;

            return directory ?? throw new InvalidOperationException("Platform.SharedKernel.slnx not found above the test output directory.");
        }

        [GeneratedRegex(@"[\\/](bin|obj|nupkgs|samples|_verification|\.git|node_modules)[\\/]", RegexOptions.IgnoreCase)]
        private static partial Regex IgnoredPath();

        [GeneratedRegex(@"^Microsoft\.Extensions\.[A-Za-z.]*Abstractions$")]
        private static partial Regex AbstractionsPackageAllowList();
    }
}

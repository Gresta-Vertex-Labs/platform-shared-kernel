using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace SharedKernel.ArchitectureTests.Tests;

/// <summary>One csproj of the repository, as the repo-graph tests and the platform observability report see it.</summary>
/// <param name="Name">The project (assembly) name.</param>
/// <param name="RelativePath">The path relative to the repository root.</param>
/// <param name="Tier">The declared <c>SharedKernelTier</c>, or <see langword="null"/>.</param>
/// <param name="IsProduction">A packable library: not a test project, not an Exe, not <c>IsPackable=false</c>.</param>
/// <param name="ProjectReferences">The direct project references (analyzer-only edges excluded).</param>
/// <param name="AllowedAdapters">The declared <c>SharedKernelAllowedAdapterReferences</c>.</param>
/// <param name="RuntimePackages">The direct runtime package references.</param>
/// <param name="AspNetCoreReferences">The ASP.NET Core framework or package references.</param>
/// <param name="AnalyzerOnlyReferences">The <c>ReferenceOutputAssembly="false"</c> references.</param>
internal sealed record ProjectNode(
    string Name,
    string RelativePath,
    string? Tier,
    bool IsProduction,
    IReadOnlyList<string> ProjectReferences,
    IReadOnlyList<string> AllowedAdapters,
    IReadOnlyList<string> RuntimePackages,
    IReadOnlyList<string> AspNetCoreReferences,
    IReadOnlyList<string> AnalyzerOnlyReferences);

/// <summary>
/// The csproj model of the repository, read from the project files and never from a list. Shared by the tier tests
/// (<c>DependencyGraphRulesTests</c>) and the platform-wide observability report.
/// </summary>
internal sealed partial class RepositoryGraph
{
    private readonly Dictionary<string, ProjectNode> _byName;

    private RepositoryGraph(DirectoryInfo root, IReadOnlyList<ProjectNode> projects)
    {
        Root = root;
        Projects = projects;
        _byName = projects.GroupBy(p => p.Name, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
    }

    public DirectoryInfo Root { get; }

    public IReadOnlyList<ProjectNode> Projects { get; }

    public ProjectNode? Find(string name) => _byName.GetValueOrDefault(name);

    public static RepositoryGraph Load()
    {
        var root = FindRepositoryRoot();
        var projects = Directory.EnumerateFiles(root.FullName, "*.csproj", SearchOption.AllDirectories)
            .Where(p => !IgnoredPath().IsMatch(p))
            .Select(p => Parse(root, p))
            .ToList();

        return new RepositoryGraph(root, projects);
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

        // A ReferenceOutputAssembly="false" edge only orders the build or loads a Roslyn component: it is not a
        // dependency, so the tier matrix exempts it (eng/SharedKernelTiers.targets, P-579). It is kept apart.
        static bool IsAnalyzerOnly(XElement e) => string.Equals(
            e.Attribute("ReferenceOutputAssembly")?.Value
                ?? e.Elements().FirstOrDefault(c => c.Name.LocalName == "ReferenceOutputAssembly")?.Value,
            "false",
            StringComparison.OrdinalIgnoreCase);
        static string ReferenceName(XElement e) =>
            Path.GetFileNameWithoutExtension((e.Attribute("Include")?.Value ?? string.Empty).Replace('\\', '/'));

        var projectReferences = document.Descendants().Where(e => e.Name.LocalName == "ProjectReference").ToList();
        var references = projectReferences.Where(e => !IsAnalyzerOnly(e))
            .Select(ReferenceName)
            .Where(n => n.Length > 0)
            .ToList();
        var analyzerOnly = projectReferences.Where(IsAnalyzerOnly)
            .Select(ReferenceName)
            .Where(n => n.Length > 0)
            .ToList();
        var packages = document.Descendants().Where(e => e.Name.LocalName == "PackageReference")
            .Where(e => !string.Equals(e.Attribute("PrivateAssets")?.Value ?? e.Elements().FirstOrDefault(c => c.Name.LocalName == "PrivateAssets")?.Value, "all", StringComparison.OrdinalIgnoreCase))
            .Select(e => e.Attribute("Include")?.Value ?? string.Empty)
            .Where(n => n.Length > 0)
            .ToList();
        var adapters = (Property("SharedKernelAllowedAdapterReferences") ?? string.Empty)
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var aspNetCore = document.Descendants()
            .Where(e => e.Name.LocalName is "FrameworkReference" or "PackageReference")
            .Select(e => e.Attribute("Include")?.Value ?? string.Empty)
            .Where(n => n == "Microsoft.AspNetCore.App" || n.StartsWith("Microsoft.AspNetCore.", StringComparison.Ordinal))
            .ToList();

        return new ProjectNode(name, Path.GetRelativePath(root.FullName, path), Property("SharedKernelTier"), isProduction, references, adapters, packages, aspNetCore, analyzerOnly);
    }

    public static DirectoryInfo FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Platform.SharedKernel.slnx")))
            directory = directory.Parent;

        return directory ?? throw new InvalidOperationException("Platform.SharedKernel.slnx not found above the test output directory.");
    }

    [GeneratedRegex(@"[\\/](bin|obj|nupkgs|samples|_verification|\.git|node_modules)[\\/]", RegexOptions.IgnoreCase)]
    private static partial Regex IgnoredPath();
}

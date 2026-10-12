using SharedKernel.ArchitectureTests.Observability;

namespace SharedKernel.ArchitectureTests.Tests;

/// <summary>
/// The platform-wide inputs of the observability assertions: every packable production project of the repository, its built DLL, the
/// domain registry and the EventId ownership tables, all read from the repository (the csproj files and the <c>CLAUDE.md</c> files), never from a list.
/// </summary>
internal static class PlatformReport
{
    /// <summary>The platform's naming pattern for an <c>ActivitySource</c> or <c>Meter</c>: <c>SharedKernel.</c> plus one PascalCase segment.</summary>
    public const string TelemetryNamePattern = @"^SharedKernel\.[A-Z]\w+$";

    /// <summary>Free-text placeholder names that can leak data into a log (root CLAUDE.md, rule L3).</summary>
    public static readonly string[] FreeTextPlaceholders = ["Error", "Reason", "ErrorMessage", "Detail", "Message"];

    /// <summary>Wire-protocol first segments of an error code (11.Communication and 14.Presentation keep <c>grpc.*</c> and <c>http.*</c>).</summary>
    public static readonly string[] ProtocolMirrorPrefixes = ["grpc", "http"];

    public static DomainRegistry Registry(RepositoryGraph graph) =>
        DomainRegistry.Parse(File.ReadAllText(Path.Combine(graph.Root.FullName, "CLAUDE.md")));

    public static IReadOnlyDictionary<string, string> AssemblyDomains(RepositoryGraph graph, DomainRegistry registry) =>
        graph.Projects.Where(p => p.IsProduction).ToDictionary(p => p.Name, p => registry.IdOf(p.RelativePath), StringComparer.Ordinal);

    public static EventIdOwnership Ownership(RepositoryGraph graph, DomainRegistry registry)
    {
        var files = registry.Domains
            .Select(d => (d.Id, Path: Path.Combine(graph.Root.FullName, d.Folder.Replace('/', Path.DirectorySeparatorChar), "CLAUDE.md")))
            .Where(f => File.Exists(f.Path))
            .Select(f => (f.Id, File.ReadAllText(f.Path)));
        return EventIdOwnership.Parse(registry, files, AssemblyDomains(graph, registry));
    }

    /// <summary>Every packable production project as a scan target; a missing DLL is <c>null</c> (reported as <c>unscanned</c>).</summary>
    public static IReadOnlyList<ScanTarget> Targets(RepositoryGraph graph) =>
        [.. graph.Projects.Where(p => p.IsProduction).Select(p => Target(graph, p))];

    public static ScanTarget TargetFor(RepositoryGraph graph, string assemblyName) =>
        Target(graph, graph.Find(assemblyName) ?? throw new InvalidOperationException($"No project named {assemblyName}."));

    /// <summary>The configuration directory the running test was built into (<c>release</c> or <c>debug</c>).</summary>
    public static string Pivot => new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)).Name;

    /// <summary>The path of a built file of a project under <c>artifacts/bin</c>, in the running configuration or the other one.</summary>
    public static string? BuiltFile(RepositoryGraph graph, string project, string fileName)
    {
        var pivots = new[] { Pivot, "release", "debug" }.Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var pivot in pivots)
        {
            var path = Path.Combine(graph.Root.FullName, "artifacts", "bin", project, pivot, fileName);
            if (File.Exists(path))
                return path;
        }

        return null;
    }

    private static ScanTarget Target(RepositoryGraph graph, ProjectNode project) =>
        new(project.Name, project.RelativePath, BuiltFile(graph, project.Name, project.Name + ".dll"));
}

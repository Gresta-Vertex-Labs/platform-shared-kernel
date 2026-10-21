using System.Text.Json;

namespace SharedKernel.ArchitectureTests.ServiceShape;

/// <summary>
/// The restore graph of a test assembly, read from its <c>.deps.json</c>: every project and package with what each one
/// references. It sees what the compiler and the runtime see, transitive packages included.
/// </summary>
public sealed class ServiceDependencyGraph
{
    private readonly Dictionary<string, string[]> _dependencies;
    private readonly HashSet<string> _projects;

    private ServiceDependencyGraph(Dictionary<string, string[]> dependencies, HashSet<string> projects)
    {
        _dependencies = dependencies;
        _projects = projects;
    }

    /// <summary>Loads the graph of the test assembly named <paramref name="testAssemblyName"/> from the test binaries folder.</summary>
    /// <param name="testAssemblyName">The assembly name of the calling test project, e.g. <c>Shop.Catalog.Tests</c>.</param>
    /// <returns>The graph.</returns>
    /// <exception cref="FileNotFoundException">The <c>.deps.json</c> is not next to the test binaries.</exception>
    public static ServiceDependencyGraph Load(string testAssemblyName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(testAssemblyName);
        string path = Path.Combine(AppContext.BaseDirectory, $"{testAssemblyName}.deps.json");
        return FromDepsJson(File.ReadAllText(path));
    }

    /// <summary>Reads a graph from the text of a <c>.deps.json</c> file.</summary>
    /// <param name="depsJson">The file content.</param>
    /// <returns>The graph.</returns>
    public static ServiceDependencyGraph FromDepsJson(string depsJson)
    {
        ArgumentNullException.ThrowIfNull(depsJson);
        using var document = JsonDocument.Parse(depsJson);
        var root = document.RootElement;

        var projects = root.GetProperty("libraries")
            .EnumerateObject()
            .Where(library => library.Value.GetProperty("type").GetString() == "project")
            .Select(library => NameOf(library.Name))
            .ToHashSet(StringComparer.Ordinal);

        var target = root.GetProperty("targets").EnumerateObject().First().Value;
        var dependencies = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var library in target.EnumerateObject())
        {
            dependencies[NameOf(library.Name)] = library.Value.TryGetProperty("dependencies", out var deps)
                ? deps.EnumerateObject().Select(dependency => dependency.Name).ToArray()
                : [];
        }

        return new ServiceDependencyGraph(dependencies, projects);
    }

    /// <summary>Builds a graph from explicit edges (for tests of rules over an in-memory graph).</summary>
    internal static ServiceDependencyGraph Create(
        IEnumerable<string> projects,
        IReadOnlyDictionary<string, string[]> dependencies
    ) =>
        new(
            dependencies.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
            projects.ToHashSet(StringComparer.Ordinal)
        );

    /// <summary>Whether <paramref name="project"/> is a project of this graph (not a package).</summary>
    /// <param name="project">The project name.</param>
    /// <returns><see langword="true"/> when the graph has the project.</returns>
    public bool ContainsProject(string project) => _projects.Contains(project);

    /// <summary>The projects <paramref name="project"/> references directly.</summary>
    /// <param name="project">The project name.</param>
    /// <returns>The directly referenced projects.</returns>
    public IReadOnlyList<string> DirectProjects(string project) => Direct(project).Where(_projects.Contains).ToList();

    /// <summary>The SharedKernel packages <paramref name="project"/> references directly.</summary>
    /// <param name="project">The project name.</param>
    /// <returns>The directly referenced kernel packages.</returns>
    public IReadOnlyList<string> DirectKernelPackages(string project) =>
        Direct(project).Where(name => !_projects.Contains(name) && KernelPackageIndex.IsKernelPackage(name)).ToList();

    /// <summary>Every project and package <paramref name="project"/> reaches, itself excluded.</summary>
    /// <param name="project">The project name.</param>
    /// <returns>The transitive closure.</returns>
    public IReadOnlyCollection<string> Closure(string project)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>(Direct(project));
        while (pending.TryPop(out string? next))
        {
            if (seen.Add(next))
            {
                foreach (string dependency in Direct(next))
                {
                    pending.Push(dependency);
                }
            }
        }

        return seen;
    }

    private IEnumerable<string> Direct(string name) => _dependencies.TryGetValue(name, out var deps) ? deps : [];

    private static string NameOf(string libraryKey) => libraryKey[..libraryKey.IndexOf('/', StringComparison.Ordinal)];
}

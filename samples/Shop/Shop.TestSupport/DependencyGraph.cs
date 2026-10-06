using System.Text.Json;

namespace Shop.TestSupport;

/// <summary>
/// The restore graph of a test assembly, read from its <c>.deps.json</c>: every project and package, with what each
/// one references. It sees what the compiler and the runtime see, transitive packages included.
/// </summary>
public sealed class DependencyGraph
{
    private readonly Dictionary<string, string[]> _dependencies;
    private readonly HashSet<string> _projects;

    private DependencyGraph(Dictionary<string, string[]> dependencies, HashSet<string> projects)
    {
        _dependencies = dependencies;
        _projects = projects;
    }

    /// <summary>Loads the graph of the test assembly named <paramref name="testAssemblyName"/>.</summary>
    public static DependencyGraph Load(string testAssemblyName)
    {
        string path = Path.Combine(AppContext.BaseDirectory, $"{testAssemblyName}.deps.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
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
            dependencies[NameOf(library.Name)] = library.Value.TryGetProperty(
                "dependencies",
                out var deps
            )
                ? deps.EnumerateObject().Select(dependency => dependency.Name).ToArray()
                : [];
        }

        return new DependencyGraph(dependencies, projects);
    }

    /// <summary>The projects <paramref name="project"/> references directly.</summary>
    public IReadOnlyList<string> DirectProjects(string project) =>
        Direct(project).Where(_projects.Contains).ToList();

    /// <summary>The SharedKernel packages <paramref name="project"/> references directly.</summary>
    public IReadOnlyList<string> DirectKernelPackages(string project) =>
        Direct(project)
            .Where(name =>
                !_projects.Contains(name)
                && name.StartsWith("SharedKernel.", StringComparison.Ordinal)
            )
            .ToList();

    /// <summary>Every project and package <paramref name="project"/> reaches, itself excluded.</summary>
    public IReadOnlyCollection<string> Closure(string project)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>(Direct(project));
        while (pending.TryPop(out var next))
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

    private IEnumerable<string> Direct(string name) =>
        _dependencies.TryGetValue(name, out var deps) ? deps : [];

    private static string NameOf(string libraryKey) =>
        libraryKey[..libraryKey.IndexOf('/', StringComparison.Ordinal)];
}

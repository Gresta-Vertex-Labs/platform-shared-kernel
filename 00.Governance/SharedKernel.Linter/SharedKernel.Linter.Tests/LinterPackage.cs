using System.Reflection;
using System.Xml.Linq;

namespace SharedKernel.Linter.Tests;

/// <summary>
/// Locates the <c>SharedKernel.Linter</c> package sources on disk and exposes them as parsed XML.
/// </summary>
/// <remarks>
/// The package produces no assembly, so there is nothing to reference and nothing to reflect over
/// — its contract is the content of the files it ships. The directory is found by walking up from
/// the test assembly until the package's own .csproj appears, rather than by counting
/// <c>..</c> segments, so the tests survive a change of target framework or output layout.
/// </remarks>
internal static class LinterPackage
{
    private const string ProjectFileName = "SharedKernel.Linter.csproj";

    static LinterPackage()
    {
        Root = Locate();
        PropsPath = new FileInfo(Path.Combine(Root.FullName, "build", "SharedKernel.Linter.props"));
        TargetsPath = new FileInfo(
            Path.Combine(Root.FullName, "build", "SharedKernel.Linter.targets")
        );
        EditorConfigPath = new FileInfo(Path.Combine(Root.FullName, "config", ".editorconfig"));
        CsprojPath = new FileInfo(Path.Combine(Root.FullName, ProjectFileName));

        Csproj = Load(CsprojPath);
        Props = Load(PropsPath);
        Targets = Load(TargetsPath);
    }

    internal static DirectoryInfo Root { get; }

    internal static FileInfo CsprojPath { get; }

    internal static FileInfo PropsPath { get; }

    internal static FileInfo TargetsPath { get; }

    internal static FileInfo EditorConfigPath { get; }

    internal static XDocument Csproj { get; }

    internal static XDocument Props { get; }

    internal static XDocument Targets { get; }

    /// <summary>
    /// Reads the last value declared for an MSBuild property in the package's own .csproj.
    /// </summary>
    /// <param name="name">The property name, e.g. <c>DevelopmentDependency</c>.</param>
    /// <returns>The declared value, or <see langword="null"/> when the property is absent.</returns>
    internal static string? Property(string name) =>
        Csproj
            .Descendants()
            .Where(element =>
                element.Name.LocalName == name && element.Parent?.Name.LocalName == "PropertyGroup"
            )
            .Select(element => element.Value)
            .LastOrDefault();

    /// <summary>
    /// Reads the shipped .editorconfig as text.
    /// </summary>
    internal static string EditorConfigText() => File.ReadAllText(EditorConfigPath.FullName);

    private static XDocument Load(FileInfo file)
    {
        if (!file.Exists)
        {
            throw new FileNotFoundException(
                $"Expected to find '{file.Name}' at '{file.FullName}'. The SharedKernel.Linter package "
                    + "layout has changed; these tests assert that layout and must be updated with it "
                    + "rather than skipped.",
                file.FullName
            );
        }

        // MSBuild files in this package are namespace-less <Project> documents; XDocument handles
        // both that and the SDK-style csproj without special casing.
        return XDocument.Load(file.FullName, LoadOptions.PreserveWhitespace);
    }

    // The test binary builds under artifacts/, outside the source tree, so the walk starts at this test
    // project's own folder (recorded at build time by the root Directory.Build.props).
    private static string WalkStart() =>
        typeof(LinterPackage).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == "SharedKernel.TestProjectDirectory")?.Value
        ?? AppContext.BaseDirectory;

    private static DirectoryInfo Locate()
    {
        var directory = new DirectoryInfo(WalkStart());

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, ProjectFileName)))
            {
                return directory;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            $"Walked up from '{WalkStart()}' without finding '{ProjectFileName}'. These tests "
                + "read the package's shipped files directly and cannot run without them."
        );
    }
}

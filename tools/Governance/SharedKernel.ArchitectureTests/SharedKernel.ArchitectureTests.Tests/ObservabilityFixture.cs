using SharedKernel.ArchitectureTests.Observability;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace SharedKernel.ArchitectureTests.Tests;

/// <summary>
/// Compiles a fixture assembly to a file for the observability assertions, which read built DLLs with Mono.Cecil and never load them.
/// Assembly names must not collide with a loaded real assembly (rule 15), so every test passes a <c>Fixture.*</c> name.
/// </summary>
internal static class ObservabilityFixture
{
    private static readonly string FixtureDirectory = Path.Combine(Path.GetTempPath(), "sharedkernel-observability-fixtures");

    /// <summary>Compiles <paramref name="source"/> against the test host's runtime, kernel and logging assemblies and returns the DLL path.</summary>
    public static string Compile(string assemblyName, string source)
    {
        var references = (((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?.Split(Path.PathSeparator) ?? [])
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path));

        var compilation = CSharpCompilation.Create(
            assemblyName,
            [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest))],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        Directory.CreateDirectory(FixtureDirectory);
        var path = Path.Combine(FixtureDirectory, $"{assemblyName}_{Guid.NewGuid():N}.dll");
        var result = compilation.Emit(path);
        if (!result.Success)
        {
            throw new InvalidOperationException(
                $"Fixture '{assemblyName}' failed to compile:{Environment.NewLine}"
                + string.Join(Environment.NewLine, result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        }

        return path;
    }

    /// <summary>A scan target for a fixture, placed in the folder of the given project path.</summary>
    public static ScanTarget Target(string assemblyName, string projectFolder, string source) =>
        new(assemblyName, $"{projectFolder}/{assemblyName}/{assemblyName}.csproj", Compile(assemblyName, source));

    /// <summary>A registry with the three domains the fixtures use.</summary>
    public static DomainRegistry Registry() =>
        DomainRegistry.Parse(
            """
            | `00.Governance` | `governance` | [`tools/Governance`](tools/Governance/) | 3 | x | To |
            | `02.Caching` | `caching` | [`src/Infrastructure/Caching`](src/Infrastructure/Caching/) | 7 | x | Ab, Ad |
            | `08.Storage` | `storage` | [`src/Infrastructure/Storage`](src/Infrastructure/Storage/) | 3 | x | Ab, Ad |
            """);

    public const string CachingFolder = "src/Infrastructure/Caching";

    public const string StorageFolder = "src/Infrastructure/Storage";
}

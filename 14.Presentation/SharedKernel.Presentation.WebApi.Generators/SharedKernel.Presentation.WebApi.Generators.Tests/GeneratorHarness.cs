using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace SharedKernel.Presentation.WebApi.Generators.Tests;

/// <summary>Compiles snippets against the real framework and WebApi assemblies and runs the generator over them.</summary>
internal static class GeneratorHarness
{
    private static readonly string WebApiPath = typeof(IEndpointModule).Assembly.Location;

    private static readonly ImmutableArray<MetadataReference> AllReferences =
        ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Where(path => path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToImmutableArray();

    public static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.Latest);

    public static CSharpCompilation Compile(bool referenceWebApi = true, params string[] sources)
    {
        var references = referenceWebApi
            ? AllReferences
            : AllReferences.Where(reference => !string.Equals(reference.Display, WebApiPath, StringComparison.OrdinalIgnoreCase)).ToImmutableArray();

        return CSharpCompilation.Create(
            "Service",
            sources.Select((source, index) => CSharpSyntaxTree.ParseText(source, ParseOptions, path: $"Source{index}.cs")),
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
    }

    public static GeneratorDriver CreateDriver() =>
        CSharpGeneratorDriver.Create(
            [new EndpointModuleGenerator().AsSourceGenerator()],
            parseOptions: ParseOptions,
            driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));

    public static GeneratorOutcome Run(bool referenceWebApi = true, params string[] sources)
    {
        var compilation = Compile(referenceWebApi, sources);
        var driver = CreateDriver().RunGeneratorsAndUpdateCompilation(compilation, out var output, out var generatorDiagnostics);
        var result = driver.GetRunResult().Results.Single();

        return new GeneratorOutcome(
            result.GeneratedSources.Select(source => source.SourceText.ToString()).SingleOrDefault(),
            generatorDiagnostics,
            output.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToImmutableArray());
    }
}

/// <summary>What one generator run produced.</summary>
/// <param name="Generated">The generated file, or <see langword="null"/> when none was generated.</param>
/// <param name="GeneratorDiagnostics">The diagnostics the generator reported.</param>
/// <param name="CompilationErrors">The errors of the compilation that includes the generated file.</param>
internal sealed record GeneratorOutcome(
    string? Generated,
    ImmutableArray<Diagnostic> GeneratorDiagnostics,
    ImmutableArray<Diagnostic> CompilationErrors);

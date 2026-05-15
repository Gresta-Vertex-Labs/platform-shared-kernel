using System.Reflection;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SharedKernel.ArchitectureTests.Rules;
using Xunit;

namespace SharedKernel.ArchitectureTests.Tests;

/// <summary>
/// Tests for <see cref="SharedKernelLayeringRules"/> — specifically
/// <see cref="SharedKernelLayeringRules.DomainNeverReferencesPersistence"/>.
/// Assemblies are compiled in-memory via Roslyn so no external fixture DLLs are needed.
/// </summary>
public class LayeringRulesTests
{
    /// <summary>
    /// T-12 (fire path): an assembly whose domain type depends on SharedKernel.Persistence
    /// must cause the rule to fail.
    /// </summary>
    [Fact]
    public void DomainNeverReferencesPersistence_ViolationAssembly_RuleFails()
    {
        // Arrange — compile an assembly where a domain type references SharedKernel.Persistence
        const string violationSource = """
            namespace SharedKernel.Persistence
            {
                public interface IRepository { }
            }

            namespace SharedKernel.Domain
            {
                public class OrderAggregate
                {
                    private readonly SharedKernel.Persistence.IRepository _repo;
                    public OrderAggregate(SharedKernel.Persistence.IRepository repo) { _repo = repo; }
                }
            }
            """;

        var violationAssembly = CompileInMemory("ViolationDomain", violationSource);

        // Act
        var conditionList = SharedKernelLayeringRules.DomainNeverReferencesPersistence(violationAssembly);
        var result = conditionList.GetResult();

        // Assert — rule must detect the forbidden dependency
        result.IsSuccessful.Should().BeFalse(
            because: "OrderAggregate references SharedKernel.Persistence.IRepository");
    }

    /// <summary>
    /// T-12 (pass path): a clean assembly with no persistence dependency must pass the rule.
    /// </summary>
    [Fact]
    public void DomainNeverReferencesPersistence_CleanAssembly_RulePasses()
    {
        // Arrange — compile an assembly with a domain type that has no Persistence reference
        const string cleanSource = """
            namespace SharedKernel.Domain
            {
                public class OrderAggregate
                {
                    public string Id { get; } = string.Empty;
                    public string Description { get; set; } = string.Empty;
                }
            }
            """;

        var cleanAssembly = CompileInMemory("CleanDomain", cleanSource);

        // Act
        var conditionList = SharedKernelLayeringRules.DomainNeverReferencesPersistence(cleanAssembly);
        var result = conditionList.GetResult();

        // Assert — no forbidden dependency present
        result.IsSuccessful.Should().BeTrue(
            because: "OrderAggregate has no reference to SharedKernel.Persistence");
    }

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Compiles C# source code and emits it to a temp file, then loads the assembly from disk.
    /// NetArchTest uses Mono.Cecil which requires a physical file path — in-memory assemblies
    /// (loaded via <c>Assembly.Load(byte[])</c>) have an empty <see cref="Assembly.Location"/>
    /// and cannot be loaded by Mono.Cecil.
    /// </summary>
    /// <param name="assemblyName">Logical name for the compiled assembly.</param>
    /// <param name="source">C# source code to compile.</param>
    /// <returns>The loaded <see cref="Assembly"/> with a valid <see cref="Assembly.Location"/>.</returns>
    private static Assembly CompileInMemory(string assemblyName, string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);

        // Reference the minimal runtime assemblies required for compilation
        var references = new[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(
                Assembly.Load("System.Runtime").Location),
        };

        var compilation = CSharpCompilation.Create(
            assemblyName,
            syntaxTrees: new[] { syntaxTree },
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );

        // Write to a temp file — NetArchTest / Mono.Cecil requires Assembly.Location to be set
        var tempPath = Path.Combine(Path.GetTempPath(), $"{assemblyName}_{Guid.NewGuid():N}.dll");
        using (var fs = File.OpenWrite(tempPath))
        {
            var emitResult = compilation.Emit(fs);

            if (!emitResult.Success)
            {
                var errors = string.Join(
                    Environment.NewLine,
                    emitResult.Diagnostics
                        .Where(d => d.Severity == DiagnosticSeverity.Error)
                        .Select(d => d.ToString()));
                throw new InvalidOperationException(
                    $"Test fixture '{assemblyName}' failed to compile:{Environment.NewLine}{errors}");
            }
        }

        return Assembly.LoadFrom(tempPath);
    }
}

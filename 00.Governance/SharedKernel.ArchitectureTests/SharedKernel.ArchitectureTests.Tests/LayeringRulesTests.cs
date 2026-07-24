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
    // T-212 — Fire path: contrived 09.Search-shaped fixture references a stubbed forbidden-domain
    // type
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-212: When a contrived "SharedKernel.Search"-shaped assembly references a type in a
    /// namespace simulating a forbidden capability domain (here, <c>SharedKernel.Persistence</c>),
    /// <see cref="SharedKernelLayeringRules.SearchReferencesOnlyCoreAndContracts"/> must fail on
    /// the corresponding array element only.
    /// </summary>
    [Fact]
    public void SearchReferencesOnlyCoreAndContracts_ViolatingAssembly_CorrespondingElementFails()
    {
        const string violationSource = """
            namespace SharedKernel.Persistence
            {
                public interface IRepository { }
            }

            namespace SharedKernel.Search
            {
                public class LeakySearchIndex
                {
                    private readonly SharedKernel.Persistence.IRepository _repository;
                    public LeakySearchIndex(SharedKernel.Persistence.IRepository repository)
                    {
                        _repository = repository;
                    }
                }
            }
            """;

        var violationAssembly = CompileInMemory("ViolationSearch", violationSource);

        var conditionLists = SharedKernelLayeringRules.SearchReferencesOnlyCoreAndContracts(violationAssembly);

        conditionLists.Should().HaveCount(15);

        var persistenceElementIndex = Array.IndexOf(ForbiddenTermsMirror, "SharedKernel.Persistence");
        persistenceElementIndex.Should().BeGreaterThanOrEqualTo(0);

        for (var i = 0; i < conditionLists.Length; i++)
        {
            var result = conditionLists[i].GetResult();
            if (i == persistenceElementIndex)
            {
                result.IsSuccessful.Should().BeFalse(
                    because: "LeakySearchIndex references SharedKernel.Persistence.IRepository directly");
            }
            else
            {
                result.IsSuccessful.Should().BeTrue(
                    because: "LeakySearchIndex has no dependency on any other forbidden capability domain");
            }
        }
    }

    // ---------------------------------------------------------------------------
    // T-213 — Pass path: contrived 09.Search-shaped fixture references only stubbed
    // SharedKernel.Primitives/SharedKernel.Contracts types
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-213: A contrived "SharedKernel.Search"-shaped assembly referencing only stubbed
    /// <c>SharedKernel.Primitives</c>/<c>SharedKernel.Contracts</c> types must pass every array
    /// element of <see cref="SharedKernelLayeringRules.SearchReferencesOnlyCoreAndContracts"/>.
    /// </summary>
    [Fact]
    public void SearchReferencesOnlyCoreAndContracts_CleanAssembly_AllElementsPass()
    {
        const string cleanSource = """
            namespace SharedKernel.Primitives
            {
                public class Result { }
            }

            namespace SharedKernel.Contracts
            {
                public class PagedList { }
            }

            namespace SharedKernel.Search
            {
                public class CleanSearchIndex
                {
                    public SharedKernel.Primitives.Result DoWork() => new SharedKernel.Primitives.Result();
                    public SharedKernel.Contracts.PagedList ToPagedList() => new SharedKernel.Contracts.PagedList();
                }
            }
            """;

        var cleanAssembly = CompileInMemory("CleanSearch", cleanSource);

        var conditionLists = SharedKernelLayeringRules.SearchReferencesOnlyCoreAndContracts(cleanAssembly);

        conditionLists.Should().HaveCount(15);
        foreach (var conditionList in conditionLists)
        {
            conditionList.GetResult().IsSuccessful.Should().BeTrue(
                because: "CleanSearchIndex depends only on SharedKernel.Primitives and SharedKernel.Contracts");
        }
    }

    // ---------------------------------------------------------------------------
    // Fixtures
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Mirrors the fifteen-term forbidden list private to
    /// <see cref="SharedKernelLayeringRules.SearchReferencesOnlyCoreAndContracts"/> — kept here,
    /// not reflected out of the production type, purely so T-212 can locate the array index that
    /// corresponds to <c>"SharedKernel.Persistence"</c> without hard-coding a fragile literal
    /// index.
    /// </summary>
    private static readonly string[] ForbiddenTermsMirror =
    [
        "SharedKernel.Caching",
        "SharedKernel.Domain",
        "SharedKernel.Application",
        "SharedKernel.Persistence",
        "SharedKernel.Messaging",
        "SharedKernel.Storage",
        "SharedKernel.AI",
        "SharedKernel.Communication",
        "SharedKernel.Security",
        "SharedKernel.ServiceDefaults",
        "SharedKernel.MultiTenancy",
        "SharedKernel.Presentation",
        "SharedKernel.Integration",
        "SharedKernel.Testing",
        "SharedKernel.Workflows",
    ];

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

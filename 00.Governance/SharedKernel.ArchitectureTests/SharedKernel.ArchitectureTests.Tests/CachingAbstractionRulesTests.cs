using System.Reflection;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SharedKernel.ArchitectureTests.Rules;
using Xunit;

namespace SharedKernel.ArchitectureTests.Tests;

/// <summary>
/// Tests for <see cref="CachingAbstractionRules.OnlyAllowedAssembliesMayReferenceConcreteCaching"/>.
/// </summary>
/// <remarks>
/// T-18 (fire path): a non-exempt assembly that references SharedKernel.Caching fails the rule.
/// T-19 (pass path): a clean assembly with no concrete caching reference passes the rule.
/// </remarks>
public class CachingAbstractionRulesTests
{
    // ---------------------------------------------------------------------------
    // T-18 — Fire path: non-exempt assembly references SharedKernel.Caching
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-18: When a non-exempt assembly contains a type that depends on <c>SharedKernel.Caching</c>,
    /// <see cref="CachingAbstractionRules.OnlyAllowedAssembliesMayReferenceConcreteCaching"/> must fail.
    /// </summary>
    [Fact]
    public void OnlyAllowedAssembliesMayReferenceConcreteCaching_ViolatingAssembly_RuleFails()
    {
        // Arrange — compile a fixture that declares a type in SharedKernel.Caching namespace
        // and another type that depends on it (simulating a concrete caching reference)
        const string violationSource = """
            namespace SharedKernel.Caching
            {
                public interface ICacheService { }
            }

            namespace Application.Services
            {
                public class OrderService
                {
                    private readonly SharedKernel.Caching.ICacheService _cache;
                    public OrderService(SharedKernel.Caching.ICacheService cache) { _cache = cache; }
                }
            }
            """;

        var violationAssembly = CompileInMemory("ViolatingCachingRef", violationSource);

        // Act
        var conditionList = CachingAbstractionRules
            .OnlyAllowedAssembliesMayReferenceConcreteCaching(violationAssembly);
        var result = conditionList.GetResult();

        // Assert
        result.IsSuccessful.Should().BeFalse(
            because: "OrderService references SharedKernel.Caching.ICacheService directly");
    }

    // ---------------------------------------------------------------------------
    // T-19 — Pass path: clean assembly with no concrete caching reference
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-19: When an assembly has no reference to <c>SharedKernel.Caching</c>, the rule passes.
    /// </summary>
    [Fact]
    public void OnlyAllowedAssembliesMayReferenceConcreteCaching_CleanAssembly_RulePasses()
    {
        // Arrange — compile a clean assembly that uses an abstraction
        const string cleanSource = """
            namespace Application.Services
            {
                public interface ICacheAbstraction { string Get(string key); }

                public class OrderService
                {
                    private readonly ICacheAbstraction _cache;
                    public OrderService(ICacheAbstraction cache) { _cache = cache; }

                    public string GetOrder(string id) => _cache.Get(id);
                }
            }
            """;

        var cleanAssembly = CompileInMemory("CleanCachingRef", cleanSource);

        // Act
        var conditionList = CachingAbstractionRules
            .OnlyAllowedAssembliesMayReferenceConcreteCaching(cleanAssembly);
        var result = conditionList.GetResult();

        // Assert
        result.IsSuccessful.Should().BeTrue(
            because: "OrderService only references an abstraction, not SharedKernel.Caching");
    }

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    private static Assembly CompileInMemory(string assemblyName, string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);

        var references = new[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(Assembly.Load("System.Runtime").Location),
        };

        var compilation = CSharpCompilation.Create(
            assemblyName,
            syntaxTrees: new[] { syntaxTree },
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );

        var tempPath = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"{assemblyName}_{System.Guid.NewGuid():N}.dll");

        using (var fs = System.IO.File.OpenWrite(tempPath))
        {
            var emitResult = compilation.Emit(fs);
            if (!emitResult.Success)
            {
                var errors = string.Join(
                    System.Environment.NewLine,
                    emitResult.Diagnostics
                        .Where(d => d.Severity == DiagnosticSeverity.Error)
                        .Select(d => d.ToString()));
                throw new InvalidOperationException(
                    $"Fixture '{assemblyName}' failed to compile:{System.Environment.NewLine}{errors}");
            }
        }

        return Assembly.LoadFrom(tempPath);
    }
}

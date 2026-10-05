using System.Reflection;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SharedKernel.ArchitectureTests.Rules;
using Xunit;

namespace SharedKernel.ArchitectureTests.Tests;

/// <summary>
/// Tests for the purity rules in <see cref="SharedKernelLayeringRules"/> and the two pipeline purity rules in
/// <see cref="ApplicationPipelineRules"/> — the dependency rules the package tiers cannot express (P-574).
/// Fixture assemblies are compiled in-memory via Roslyn; every rule is also run against the real assembly.
/// </summary>
public class LayeringRulesTests
{
    // ---------------------------------------------------------------------------
    // ContractsNeverReferencesDomain — both Model tier
    // ---------------------------------------------------------------------------

    [Fact]
    public void ContractsNeverReferencesDomain_DomainDependency_RuleFails()
    {
        const string violationSource = """
            namespace SharedKernel.Domain
            {
                public interface IDomainEvent { }
            }

            namespace SharedKernel.Contracts
            {
                public sealed class OrderPlacedDto
                {
                    public SharedKernel.Domain.IDomainEvent? Source { get; set; }
                }
            }
            """;

        var result = SharedKernelLayeringRules
            .ContractsNeverReferencesDomain(CompileInMemory("ContractsDomainViolation", violationSource))
            .GetResult();

        result.IsSuccessful.Should().BeFalse(because: "OrderPlacedDto references SharedKernel.Domain");
    }

    [Fact]
    public void ContractsNeverReferencesDomain_PrimitivesDependencyOnly_RulePasses()
    {
        const string cleanSource = """
            namespace SharedKernel.Primitives
            {
                public sealed class Error { }
            }

            namespace SharedKernel.Contracts
            {
                public sealed class PageRequest
                {
                    public static SharedKernel.Primitives.Error? Validate(int page) => page < 1 ? new SharedKernel.Primitives.Error() : null;
                }
            }
            """;

        var result = SharedKernelLayeringRules
            .ContractsNeverReferencesDomain(CompileInMemory("ContractsClean", cleanSource))
            .GetResult();

        result.IsSuccessful.Should().BeTrue(because: "PageRequest depends only on SharedKernel.Primitives");
    }

    [Fact]
    public void ContractsNeverReferencesDomain_RealContractsAssembly_RulePasses()
    {
        var result = SharedKernelLayeringRules
            .ContractsNeverReferencesDomain(typeof(SharedKernel.Contracts.Events.EventEnvelope).Assembly)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(because: "SharedKernel.Contracts references only SharedKernel.Primitives");
    }

    // ---------------------------------------------------------------------------
    // DomainNeverReferencesContracts — both Model tier
    // ---------------------------------------------------------------------------

    [Fact]
    public void DomainNeverReferencesContracts_ContractsDependency_RuleFails()
    {
        const string violationSource = """
            namespace SharedKernel.Contracts
            {
                public sealed class OrderPlacedDto { }
            }

            namespace SharedKernel.Domain
            {
                public class OrderAggregate
                {
                    public SharedKernel.Contracts.OrderPlacedDto ToWire() => new();
                }
            }
            """;

        var result = SharedKernelLayeringRules
            .DomainNeverReferencesContracts(CompileInMemory("DomainContractsViolation", violationSource))
            .GetResult();

        result.IsSuccessful.Should().BeFalse(because: "OrderAggregate references SharedKernel.Contracts");
    }

    [Fact]
    public void DomainNeverReferencesContracts_CleanAssembly_RulePasses()
    {
        const string cleanSource = """
            namespace SharedKernel.Domain
            {
                public class OrderAggregate
                {
                    public string Id { get; } = string.Empty;
                }
            }
            """;

        var result = SharedKernelLayeringRules
            .DomainNeverReferencesContracts(CompileInMemory("DomainClean", cleanSource))
            .GetResult();

        result.IsSuccessful.Should().BeTrue(because: "OrderAggregate has no reference to SharedKernel.Contracts");
    }

    [Fact]
    public void DomainNeverReferencesContracts_RealDomainAssembly_RulePasses()
    {
        var result = SharedKernelLayeringRules
            .DomainNeverReferencesContracts(typeof(SharedKernel.Domain.Entities.Entity<>).Assembly)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(because: "SharedKernel.Domain never references SharedKernel.Contracts");
    }

    // ---------------------------------------------------------------------------
    // ModelNeverReferencesLogging — Domain and Contracts stay logging-free
    // ---------------------------------------------------------------------------

    [Fact]
    public void ModelNeverReferencesLogging_LoggerDependency_RuleFails()
    {
        const string violationSource = """
            namespace Microsoft.Extensions.Logging
            {
                public interface ILogger { }
            }

            namespace SharedKernel.Domain
            {
                public class OrderAggregate
                {
                    public OrderAggregate(Microsoft.Extensions.Logging.ILogger logger) { }
                }
            }
            """;

        var result = SharedKernelLayeringRules
            .ModelNeverReferencesLogging(CompileInMemory("DomainLoggingViolation", violationSource))
            .GetResult();

        result.IsSuccessful.Should().BeFalse(because: "OrderAggregate takes an ILogger");
    }

    [Theory]
    [InlineData(typeof(SharedKernel.Domain.Entities.Entity<>))]
    [InlineData(typeof(SharedKernel.Contracts.Events.EventEnvelope))]
    public void ModelNeverReferencesLogging_RealModelAssemblies_RulePass(Type anchor)
    {
        var result = SharedKernelLayeringRules.ModelNeverReferencesLogging(anchor.Assembly).GetResult();

        result.IsSuccessful.Should().BeTrue(because: $"{anchor.Assembly.GetName().Name} is logging-free");
    }

    // ---------------------------------------------------------------------------
    // PipelineNeverReferencesCachingPollyOrHosting — P-544
    // ---------------------------------------------------------------------------

    [Fact]
    public void PipelineNeverReferencesCachingPollyOrHosting_CachingAbstractionsDependency_RuleFails()
    {
        const string violationSource = """
            namespace SharedKernel.Caching.Abstractions
            {
                public interface ICacheService { }
            }

            namespace SharedKernel.Application.Pipeline
            {
                public sealed class LeakyBehavior
                {
                    public SharedKernel.Caching.Abstractions.ICacheService? Cache { get; set; }
                }
            }
            """;

        var result = ApplicationPipelineRules
            .PipelineNeverReferencesCachingPollyOrHosting(CompileInMemory("PipelineCachingViolation", violationSource))
            .GetResult();

        result.IsSuccessful.Should().BeFalse(
            because: "LeakyBehavior references SharedKernel.Caching.Abstractions, which belongs to SharedKernel.Application.Pipeline.Caching");
    }

    [Fact]
    public void PipelineNeverReferencesCachingPollyOrHosting_CleanAssembly_RulePasses()
    {
        const string cleanSource = """
            namespace SharedKernel.Application.Pipeline
            {
                public sealed class CleanBehavior
                {
                    public string Name { get; } = string.Empty;
                }
            }
            """;

        var result = ApplicationPipelineRules
            .PipelineNeverReferencesCachingPollyOrHosting(CompileInMemory("PipelineClean", cleanSource))
            .GetResult();

        result.IsSuccessful.Should().BeTrue(because: "CleanBehavior has no dependency on Caching, Polly or Hosting");
    }

    /// <summary>
    /// P-579: the pipeline turns a failed result into its exception with <c>SharedKernel.Core</c>'s
    /// <c>error.ToException()</c>, so a Core dependency is allowed.
    /// </summary>
    [Fact]
    public void PipelineNeverReferencesCachingPollyOrHosting_CoreDependency_RulePasses()
    {
        const string coreSource = """
            namespace SharedKernel.Core.Extensions
            {
                public static class ErrorExtensions
                {
                    public static System.Exception ToException(this string error) => new System.InvalidOperationException(error);
                }
            }

            namespace SharedKernel.Application.Pipeline
            {
                public sealed class ThrowingBehavior
                {
                    public System.Exception Fail(string error) => SharedKernel.Core.Extensions.ErrorExtensions.ToException(error);
                }
            }
            """;

        var result = ApplicationPipelineRules
            .PipelineNeverReferencesCachingPollyOrHosting(CompileInMemory("PipelineCoreAllowed", coreSource))
            .GetResult();

        result.IsSuccessful.Should().BeTrue(because: "SharedKernel.Core is an allowed dependency of the pipeline since P-579");
    }

    [Fact]
    public void PipelineNeverReferencesCachingPollyOrHosting_RealPipelineAssembly_RulePasses()
    {
        var result = ApplicationPipelineRules
            .PipelineNeverReferencesCachingPollyOrHosting(
                typeof(SharedKernel.Application.Pipeline.ApplicationPipelineBuilder).Assembly)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(because: "SharedKernel.Application.Pipeline carries no cache, Polly or hosting dependency");
    }

    // ---------------------------------------------------------------------------
    // PipelineCachingNeverReferencesConcreteInfrastructure — P-544
    // ---------------------------------------------------------------------------

    [Fact]
    public void PipelineCachingNeverReferencesConcreteInfrastructure_RedisDependency_RuleFails()
    {
        const string violationSource = """
            namespace SharedKernel.Caching.Redis
            {
                public interface IConnectionMultiplexerAdapter { }
            }

            namespace SharedKernel.Application.Pipeline.Caching
            {
                public sealed class LeakyCachingBehavior
                {
                    public SharedKernel.Caching.Redis.IConnectionMultiplexerAdapter? Redis { get; set; }
                }
            }
            """;

        var result = ApplicationPipelineRules
            .PipelineCachingNeverReferencesConcreteInfrastructure(CompileInMemory("PipelineCachingRedisViolation", violationSource))
            .GetResult();

        result.IsSuccessful.Should().BeFalse(because: "LeakyCachingBehavior references the concrete SharedKernel.Caching.Redis package");
    }

    [Fact]
    public void PipelineCachingNeverReferencesConcreteInfrastructure_AbstractionsOnly_RulePasses()
    {
        const string cleanSource = """
            namespace SharedKernel.Caching.Abstractions
            {
                public interface ICacheService { }
            }

            namespace SharedKernel.Application.Pipeline.Caching
            {
                public sealed class CleanCachingBehavior
                {
                    public SharedKernel.Caching.Abstractions.ICacheService? Cache { get; set; }
                }
            }
            """;

        var result = ApplicationPipelineRules
            .PipelineCachingNeverReferencesConcreteInfrastructure(CompileInMemory("PipelineCachingAbstractionsOnly", cleanSource))
            .GetResult();

        result.IsSuccessful.Should().BeTrue(because: "CleanCachingBehavior depends only on SharedKernel.Caching.Abstractions");
    }

    [Fact]
    public void PipelineCachingNeverReferencesConcreteInfrastructure_RealPipelineCachingAssembly_RulePasses()
    {
        var result = ApplicationPipelineRules
            .PipelineCachingNeverReferencesConcreteInfrastructure(
                typeof(SharedKernel.Application.Pipeline.Caching.CachingPipelineExtensions).Assembly)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(because: "the caching pipeline reaches SharedKernel.Caching.Abstractions only");
    }

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Compiles C# source code and emits it to a temp file, then loads the assembly from disk.
    /// NetArchTest uses Mono.Cecil, which requires a physical file path.
    /// </summary>
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
            syntaxTrees: [syntaxTree],
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

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

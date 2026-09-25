using System.Reflection;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SharedKernel.ArchitectureTests.Rules;
using Xunit;

namespace SharedKernel.ArchitectureTests.Tests;

/// <summary>
/// Tests for <see cref="ApplicationPipelineRules"/> — the 05.Application extended-pipeline
/// enforcement predicates introduced by WO-036 P-225.
/// </summary>
/// <remarks>
/// T-147/T-148: <see cref="ApplicationPipelineRules.BehaviorsNeverReferenceConcreteInfrastructure"/>
/// T-149/T-150: <see cref="ApplicationPipelineRules.NoExistingBehaviorMatchesStreamRequestConstraint"/>
/// </remarks>
public class ApplicationPipelineRulesTests
{
    // ---------------------------------------------------------------------------
    // T-147 — Fire path: named behavior references a forbidden concrete-infrastructure namespace
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-147: A contrived fixture with a type named <c>TracingBehavior</c> referencing a
    /// forbidden concrete-infrastructure namespace (<c>SharedKernel.Caching.Redis</c>) must
    /// fail <see cref="ApplicationPipelineRules.BehaviorsNeverReferenceConcreteInfrastructure"/>.
    /// </summary>
    [Fact]
    public void BehaviorsNeverReferenceConcreteInfrastructure_ConcreteRedisReference_RuleFails()
    {
        const string redisStubSource = """
            namespace SharedKernel.Caching.Redis
            {
                public interface IConnectionMultiplexerAdapter { }
            }
            """;

        const string behaviorSource = """
            namespace SharedKernel.Application.Pipeline.Tracing
            {
                public class TracingBehavior
                {
                    private readonly SharedKernel.Caching.Redis.IConnectionMultiplexerAdapter _redis;

                    public TracingBehavior(SharedKernel.Caching.Redis.IConnectionMultiplexerAdapter redis)
                    {
                        _redis = redis;
                    }
                }
            }
            """;

        var redisAssembly = CompileInMemory(
            "Fixture.AppPipeline.SharedKernel.Caching.Redis.Stub",
            redisStubSource);

        var behaviorAssembly = CompileInMemory(
            "Fixture.AppPipeline.ViolatingTracingBehavior",
            behaviorSource,
            extraReferences: new[] { redisAssembly });

        var result = ApplicationPipelineRules
            .BehaviorsNeverReferenceConcreteInfrastructure(behaviorAssembly)
            .GetResult();

        result.IsSuccessful.Should().BeFalse(
            because: "TracingBehavior depends on SharedKernel.Caching.Redis — a forbidden " +
                     "concrete-infrastructure namespace for named pipeline behaviors");
    }

    // ---------------------------------------------------------------------------
    // T-148 — Pass path: named behaviors reference only permitted namespaces
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-148: A contrived fixture with the same two behavior names referencing only
    /// permitted namespaces (<c>SharedKernel.Caching.Abstractions</c>,
    /// <c>SharedKernel.Persistence.Abstractions</c>) must pass
    /// <see cref="ApplicationPipelineRules.BehaviorsNeverReferenceConcreteInfrastructure"/>.
    /// </summary>
    [Fact]
    public void BehaviorsNeverReferenceConcreteInfrastructure_AbstractionsOnly_RulePasses()
    {
        const string abstractionsStubSource = """
            namespace SharedKernel.Caching.Abstractions
            {
                public interface ICacheService { }
            }

            namespace SharedKernel.Persistence.Abstractions
            {
                public interface IUnitOfWork { }
            }
            """;

        const string behaviorsSource = """
            namespace SharedKernel.Application.Pipeline.Tracing
            {
                public class TracingBehavior
                {
                    private readonly SharedKernel.Caching.Abstractions.ICacheService _cache;

                    public TracingBehavior(SharedKernel.Caching.Abstractions.ICacheService cache)
                    {
                        _cache = cache;
                    }
                }
            }

            namespace SharedKernel.Application.Pipeline.Caching
            {
                public class CacheInvalidationBehavior
                {
                    private readonly SharedKernel.Caching.Abstractions.ICacheService _cache;

                    public CacheInvalidationBehavior(SharedKernel.Caching.Abstractions.ICacheService cache)
                    {
                        _cache = cache;
                    }
                }
            }
            """;

        var abstractionsAssembly = CompileInMemory(
            "Fixture.AppPipeline.Abstractions.Stub",
            abstractionsStubSource);

        var behaviorsAssembly = CompileInMemory(
            "Fixture.AppPipeline.CompliantBehaviors",
            behaviorsSource,
            extraReferences: new[] { abstractionsAssembly });

        var result = ApplicationPipelineRules
            .BehaviorsNeverReferenceConcreteInfrastructure(behaviorsAssembly)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "both named behaviors reference only .Abstractions namespaces");
    }

    // ---------------------------------------------------------------------------
    // T-149 — Fire path: IPipelineBehavior<,> implementor's TRequest constraint matches
    //          IStreamQuery<TResponse> structurally
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-149: A contrived fixture with an <c>IPipelineBehavior&lt;,&gt;</c> implementor whose
    /// <c>TRequest</c> constraint structurally satisfies <c>IStreamQuery&lt;TResponse&gt;</c>
    /// must fail
    /// <see cref="ApplicationPipelineRules.NoExistingBehaviorMatchesStreamRequestConstraint"/>.
    /// </summary>
    [Fact]
    public void NoExistingBehaviorMatchesStreamRequestConstraint_StreamRequestConstraint_RuleFails()
    {
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;
            using SharedKernel.Application.Messaging;
            using SharedKernel.Application.Streaming;

            namespace SharedKernel.Application.Pipeline.Violations
            {
                public class LooseStreamingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
                    where TRequest : notnull, IStreamQuery<TResponse>
                {
                    public Task<TResponse> Handle(
                        TRequest request,
                        RequestHandlerContinuation<TResponse> next,
                        CancellationToken cancellationToken)
                    {
                        return next();
                    }
                }
            }
            """;

        var assembly = CompileInMemory(
            "Fixture.AppPipeline.StreamConstraintViolation",
            source);

        var result = ApplicationPipelineRules
            .NoExistingBehaviorMatchesStreamRequestConstraint(assembly)
            .GetResult();

        result.IsSuccessful.Should().BeFalse(
            because: "LooseStreamingBehavior's TRequest constraint structurally satisfies " +
                     "SharedKernel.Application.Streaming.IStreamQuery<TResponse> — exactly the loosening this rule forecloses");
    }

    // ---------------------------------------------------------------------------
    // T-150 — Pass path: behavior constraint shapes mirror the real WO-035/WO-036 shapes
    //          (IRequest<TResponse>-rooted only)
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-150: A contrived fixture mirroring the real WO-035/WO-036 behavior constraint shapes
    /// (<c>IRequest&lt;TResponse&gt;</c>-rooted only) must pass
    /// <see cref="ApplicationPipelineRules.NoExistingBehaviorMatchesStreamRequestConstraint"/>.
    /// </summary>
    [Fact]
    public void NoExistingBehaviorMatchesStreamRequestConstraint_RequestRootedOnly_RulePasses()
    {
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;
            using SharedKernel.Application.Messaging;
            using SharedKernel.Application.Streaming;

            namespace SharedKernel.Application.Pipeline.Compliant
            {
                public class ValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
                    where TRequest : notnull, IRequest<TResponse>
                {
                    public Task<TResponse> Handle(
                        TRequest request,
                        RequestHandlerContinuation<TResponse> next,
                        CancellationToken cancellationToken)
                    {
                        return next();
                    }
                }
            }
            """;

        var assembly = CompileInMemory(
            "Fixture.AppPipeline.RequestRootedConstraint",
            source);

        var result = ApplicationPipelineRules
            .NoExistingBehaviorMatchesStreamRequestConstraint(assembly)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "ValidationBehavior's TRequest constraint is IRequest<TResponse>-rooted " +
                     "only and never structurally matches IStreamQuery<TResponse>");
    }

    /// <summary>
    /// P-567: the shipped <c>SharedKernel.Application.Pipeline</c> and
    /// <c>SharedKernel.Application.Pipeline.Caching</c> assemblies pass both rules — no request behavior
    /// captures a stream, and neither named behavior reaches concrete infrastructure.
    /// </summary>
    [Fact]
    public void ShippedPipelineAssemblies_PassBothRules()
    {
        var pipeline = typeof(SharedKernel.Application.Pipeline.Extensions.ApplicationBehaviorsBuilder).Assembly;
        var caching = typeof(SharedKernel.Application.Pipeline.Caching.Extensions.CachingBehaviorsExtensions).Assembly;

        ApplicationPipelineRules.NoExistingBehaviorMatchesStreamRequestConstraint(pipeline).GetResult().IsSuccessful.Should().BeTrue();
        ApplicationPipelineRules.NoExistingBehaviorMatchesStreamRequestConstraint(caching).GetResult().IsSuccessful.Should().BeTrue();
        ApplicationPipelineRules.BehaviorsNeverReferenceConcreteInfrastructure(pipeline, caching).GetResult().IsSuccessful.Should().BeTrue();
    }

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Compiles <paramref name="source"/> into an in-memory assembly for NetArchTest scanning.
    /// </summary>
    /// <remarks>
    /// Follows the pattern established in <see cref="CommunicationLayeringRulesTests"/> /
    /// <see cref="RedisTopologyRulesTests"/>: extra references are provided via
    /// <c>MetadataReference.CreateFromImage</c> from the in-memory bytes (for fixture-to-fixture
    /// chaining) or <c>MetadataReference.CreateFromFile</c> (for real on-disk assemblies like
    /// SharedKernel.Application) to avoid <c>CS0234</c> failures.
    /// </remarks>
    private static Assembly CompileInMemory(
        string assemblyName,
        string source,
        Assembly[]? extraReferences = null)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);

        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(Assembly.Load("System.Runtime").Location),
            MetadataReference.CreateFromFile(Assembly.Load("System.Console").Location),
            MetadataReference.CreateFromFile(typeof(Task).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(SharedKernel.Application.Messaging.IRequest<>).Assembly.Location),
        };

        if (extraReferences is not null)
        {
            foreach (var extraReference in extraReferences)
            {
                if (string.IsNullOrEmpty(extraReference.Location))
                {
                    continue;
                }

                if (File.Exists(extraReference.Location))
                {
                    references.Add(MetadataReference.CreateFromFile(extraReference.Location));
                }
                else
                {
                    references.Add(
                        MetadataReference.CreateFromImage(
                            System.Collections.Immutable.ImmutableArray.Create(
                                File.ReadAllBytes(extraReference.Location))));
                }
            }
        }

        var compilation = CSharpCompilation.Create(
            assemblyName,
            syntaxTrees: new[] { syntaxTree },
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var tempPath = Path.Combine(
            Path.GetTempPath(),
            $"{assemblyName}_{Guid.NewGuid():N}.dll");

        using (var stream = new MemoryStream())
        {
            var emitResult = compilation.Emit(stream);
            if (!emitResult.Success)
            {
                var errors = string.Join(
                    Environment.NewLine,
                    emitResult.Diagnostics
                        .Where(d => d.Severity == DiagnosticSeverity.Error)
                        .Select(d => d.ToString()));
                throw new InvalidOperationException(
                    $"Fixture '{assemblyName}' failed to compile:{Environment.NewLine}{errors}");
            }

            stream.Seek(0, SeekOrigin.Begin);
            File.WriteAllBytes(tempPath, stream.ToArray());
        }

        return Assembly.LoadFrom(tempPath);
    }
}

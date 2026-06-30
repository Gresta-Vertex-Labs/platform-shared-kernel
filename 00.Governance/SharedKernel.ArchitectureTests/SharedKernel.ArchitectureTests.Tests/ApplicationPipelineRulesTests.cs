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
/// T-151/T-152: <see cref="ApplicationPipelineRules.NoHandRolledRetryLoopOutsideResilienceBehavior"/>
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
            namespace SharedKernel.Application.Behaviors.Tracing
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
    /// T-148: A contrived fixture with the same three behavior names referencing only
    /// permitted namespaces (<c>SharedKernel.Caching.Abstractions</c>,
    /// <c>SharedKernel.Primitives</c>, <c>MediatR</c>) must pass
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

            namespace SharedKernel.Messaging.Abstractions
            {
                public interface IMessageBus { }
            }
            """;

        const string behaviorsSource = """
            namespace SharedKernel.Application.Behaviors.Tracing
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

            namespace SharedKernel.Application.Behaviors.Resilience
            {
                public class ResilienceBehavior
                {
                    private readonly SharedKernel.Messaging.Abstractions.IMessageBus _bus;

                    public ResilienceBehavior(SharedKernel.Messaging.Abstractions.IMessageBus bus)
                    {
                        _bus = bus;
                    }
                }
            }

            namespace SharedKernel.Application.Behaviors.CacheInvalidation
            {
                public class CacheInvalidationBehavior
                {
                    private readonly SharedKernel.Persistence.Abstractions.IUnitOfWork _uow;

                    public CacheInvalidationBehavior(SharedKernel.Persistence.Abstractions.IUnitOfWork uow)
                    {
                        _uow = uow;
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
            because: "all three named behaviors reference only .Abstractions namespaces");
    }

    // ---------------------------------------------------------------------------
    // T-149 — Fire path: IPipelineBehavior<,> implementor's TRequest constraint matches
    //          IStreamRequest<TResponse> structurally
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-149: A contrived fixture with an <c>IPipelineBehavior&lt;,&gt;</c> implementor whose
    /// <c>TRequest</c> constraint structurally satisfies <c>IStreamRequest&lt;TResponse&gt;</c>
    /// must fail
    /// <see cref="ApplicationPipelineRules.NoExistingBehaviorMatchesStreamRequestConstraint"/>.
    /// </summary>
    [Fact]
    public void NoExistingBehaviorMatchesStreamRequestConstraint_StreamRequestConstraint_RuleFails()
    {
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;
            using MediatR;

            namespace SharedKernel.Application.Behaviors.Violations
            {
                public class LooseStreamingBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
                    where TRequest : notnull, IStreamRequest<TResponse>
                {
                    public Task<TResponse> Handle(
                        TRequest request,
                        RequestHandlerDelegate<TResponse> next,
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
                     "MediatR.IStreamRequest<TResponse> — exactly the loosening this rule forecloses");
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
            using MediatR;

            namespace SharedKernel.Application.Behaviors.Compliant
            {
                public class ValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
                    where TRequest : notnull, IRequest<TResponse>
                {
                    public Task<TResponse> Handle(
                        TRequest request,
                        RequestHandlerDelegate<TResponse> next,
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
                     "only and never structurally matches IStreamRequest<TResponse>");
    }

    // ---------------------------------------------------------------------------
    // T-151 — Fire path: Task.Delay inside a loop in a type NOT named ResilienceBehavior
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-151: A contrived fixture with a <c>Task.Delay</c> call inside a loop in a type NOT
    /// named <c>ResilienceBehavior</c> must fail
    /// <see cref="ApplicationPipelineRules.NoHandRolledRetryLoopOutsideResilienceBehavior"/>.
    /// </summary>
    /// <remarks>
    /// Uses a synchronous <c>Task.Delay(...).GetAwaiter().GetResult()</c> call rather than
    /// <c>await</c> — the C# compiler lowers <c>async</c> methods into a
    /// <c>[CompilerGenerated]</c> nested state-machine type, and NetArchTest's
    /// <c>Types.GetAllTypes</c> deliberately excludes <c>[CompilerGenerated]</c> types from
    /// its scan (confirmed via direct Mono.Cecil inspection of <c>NetArchTest.Rules.dll</c>),
    /// so an <c>await Task.Delay(...)</c> call site would never be visible to this predicate.
    /// The synchronous form keeps the call in the declaring type's own method body, which is
    /// the shape this IL-level fingerprint heuristic is designed to detect.
    /// </remarks>
    [Fact]
    public void NoHandRolledRetryLoopOutsideResilienceBehavior_HandRolledRetryLoop_RuleFails()
    {
        const string source = """
            using System.Threading.Tasks;

            namespace SharedKernel.Application.Behaviors.Violations
            {
                public class AdHocRetryBehavior
                {
                    public void RunWithRetry()
                    {
                        for (var attempt = 0; attempt < 3; attempt++)
                        {
                            Task.Delay(100).GetAwaiter().GetResult();
                        }
                    }
                }
            }
            """;

        var assembly = CompileInMemory("Fixture.AppPipeline.HandRolledRetryLoop", source);

        var result = ApplicationPipelineRules
            .NoHandRolledRetryLoopOutsideResilienceBehavior(assembly)
            .GetResult();

        result.IsSuccessful.Should().BeFalse(
            because: "AdHocRetryBehavior calls Task.Delay inside a hand-rolled retry loop and " +
                     "is not named ResilienceBehavior");
    }

    // ---------------------------------------------------------------------------
    // T-152 — Pass path: Task.Delay only inside ResilienceBehavior, and vacuous-pass
    //          companion case with no Task.Delay call at all
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-152: A contrived fixture with <c>Task.Delay</c> only inside a type named
    /// <c>ResilienceBehavior</c> must pass
    /// <see cref="ApplicationPipelineRules.NoHandRolledRetryLoopOutsideResilienceBehavior"/>.
    /// </summary>
    [Fact]
    public void NoHandRolledRetryLoopOutsideResilienceBehavior_DelayInsideResilienceBehavior_RulePasses()
    {
        const string source = """
            using System.Threading.Tasks;

            namespace SharedKernel.Application.Behaviors.Resilience
            {
                public class ResilienceBehavior
                {
                    public void RunWithRetry()
                    {
                        for (var attempt = 0; attempt < 3; attempt++)
                        {
                            Task.Delay(100).GetAwaiter().GetResult();
                        }
                    }
                }
            }
            """;

        var assembly = CompileInMemory("Fixture.AppPipeline.DelayInResilienceBehavior", source);

        var result = ApplicationPipelineRules
            .NoHandRolledRetryLoopOutsideResilienceBehavior(assembly)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "the only Task.Delay call site is inside ResilienceBehavior, the " +
                     "self-exempt platform-sanctioned resilience pipeline");
    }

    /// <summary>
    /// T-152 companion (vacuous pass): a contrived fixture with no <c>Task.Delay</c> call at
    /// all must pass
    /// <see cref="ApplicationPipelineRules.NoHandRolledRetryLoopOutsideResilienceBehavior"/>.
    /// </summary>
    [Fact]
    public void NoHandRolledRetryLoopOutsideResilienceBehavior_NoDelayCallAtAll_RulePasses()
    {
        const string source = """
            namespace SharedKernel.Application.Behaviors.Compliant
            {
                public class LoggingBehavior
                {
                    public void Log(string message)
                    {
                        System.Console.WriteLine(message);
                    }
                }
            }
            """;

        var assembly = CompileInMemory("Fixture.AppPipeline.NoDelayAtAll", source);

        var result = ApplicationPipelineRules
            .NoHandRolledRetryLoopOutsideResilienceBehavior(assembly)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "no type in the assembly calls Task.Delay at all (vacuous pass)");
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
    /// MediatR) to avoid <c>CS0234</c> failures.
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
            MetadataReference.CreateFromFile(typeof(MediatR.IBaseRequest).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(MediatR.IPipelineBehavior<,>).Assembly.Location),
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

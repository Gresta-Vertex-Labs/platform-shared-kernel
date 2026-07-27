using System.Reflection;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SharedKernel.ArchitectureTests.Rules;
using Xunit;

namespace SharedKernel.ArchitectureTests.Tests;

/// <summary>
/// Tests for <see cref="WorkflowTopologyRules"/> — the <c>17.Workflows</c> package topology
/// enforcement predicates introduced by WO-046 P-290.
/// </summary>
/// <remarks>
/// <para>
/// T-246/T-247/T-248 cover <see cref="WorkflowTopologyRules.NoRawClientAccessorConsumptionInRepo"/> —
/// a constructor-parameter fire path, a field fire path, and a pass path proving a factory delegate
/// that PRODUCES an <c>ITemporalRawClientAccessor</c> (rather than consuming one) is never a false
/// positive. T-249/T-250 cover
/// <see cref="WorkflowTopologyRules.NoHealthChecksDependencyInWorkflows"/>.
/// </para>
/// <para>
/// Every fire-path/pass-path pair uses CONTRIVED in-memory fixture assemblies — the primary
/// red/green proof, per the "designed-ahead-of-a-pending-dependency" precedent this domain has
/// followed since <c>RedisTopologyRulesTests</c>/<c>StorageTopologyRulesTests</c>/
/// <c>SearchTopologyRulesTests</c>/<c>IntelligenceTopologyRulesTests</c>. Additional
/// <c>Real*</c>-suffixed tests below re-run the same predicates against the REAL, now-Published
/// <c>SharedKernel.Workflows.Temporal</c> assembly — this phase's own acceptance criteria treat
/// real-assembly verification as GATING, and <c>17.Workflows</c> reached Published (all six phases
/// <c>●</c>) before this phase's implementation session, so real-assembly verification is wired now
/// rather than deferred.
/// </para>
/// </remarks>
public class WorkflowTopologyRulesTests
{
    // ---------------------------------------------------------------------------
    // T-246 — Fire path: constructor-parameter consumption of ITemporalRawClientAccessor
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-246: A type whose constructor injects a type named exactly
    /// <c>ITemporalRawClientAccessor</c> must fail
    /// <see cref="WorkflowTopologyRules.NoRawClientAccessorConsumptionInRepo"/>.
    /// </summary>
    [Fact]
    public void NoRawClientAccessorConsumptionInRepo_ConstructorParameterConsumption_RuleFails()
    {
        const string accessorStubSource = """
            namespace SharedKernel.Workflows.Temporal.Hosting
            {
                public interface ITemporalRawClientAccessor
                {
                }
            }
            """;

        const string consumerSource = """
            namespace Fixture.RawAccessorConsumer.CtorParam
            {
                public class VisibilityQueryService
                {
                    public VisibilityQueryService(SharedKernel.Workflows.Temporal.Hosting.ITemporalRawClientAccessor accessor)
                    {
                    }
                }
            }
            """;

        var accessorStubAssembly = CompileInMemory(
            "Fixture.RawAccessorCtorTest.Accessor",
            accessorStubSource);
        var consumerAssembly = CompileInMemory(
            "ViolatingRawAccessorCtorConsumer",
            consumerSource,
            extraReferences: new[] { accessorStubAssembly });

        var conditionList = WorkflowTopologyRules.NoRawClientAccessorConsumptionInRepo(consumerAssembly);
        var result = conditionList.GetResult();

        result.IsSuccessful.Should().BeFalse(
            because: "VisibilityQueryService injects ITemporalRawClientAccessor as a constructor parameter");
    }

    // ---------------------------------------------------------------------------
    // T-247 — Fire path: field consumption of ITemporalRawClientAccessor
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-247: A type carrying a field typed exactly <c>ITemporalRawClientAccessor</c> must fail
    /// <see cref="WorkflowTopologyRules.NoRawClientAccessorConsumptionInRepo"/>.
    /// </summary>
    [Fact]
    public void NoRawClientAccessorConsumptionInRepo_FieldConsumption_RuleFails()
    {
        const string accessorStubSource = """
            namespace SharedKernel.Workflows.Temporal.Hosting
            {
                public interface ITemporalRawClientAccessor
                {
                }
            }
            """;

        const string consumerSource = """
            namespace Fixture.RawAccessorConsumer.Field
            {
                public class ScheduleAdministrationHelper
                {
                    private SharedKernel.Workflows.Temporal.Hosting.ITemporalRawClientAccessor? _accessor;
                }
            }
            """;

        var accessorStubAssembly = CompileInMemory(
            "Fixture.RawAccessorFieldTest.Accessor",
            accessorStubSource);
        var consumerAssembly = CompileInMemory(
            "ViolatingRawAccessorFieldConsumer",
            consumerSource,
            extraReferences: new[] { accessorStubAssembly });

        var conditionList = WorkflowTopologyRules.NoRawClientAccessorConsumptionInRepo(consumerAssembly);
        var result = conditionList.GetResult();

        result.IsSuccessful.Should().BeFalse(
            because: "ScheduleAdministrationHelper carries a field typed ITemporalRawClientAccessor");
    }

    // ---------------------------------------------------------------------------
    // T-248 — Pass path: a factory delegate that PRODUCES an ITemporalRawClientAccessor (never
    // consumes one as a ctor/field dependency) is not a false positive
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-248: A type whose method PRODUCES (constructs and returns) an
    /// <c>ITemporalRawClientAccessor</c> — the shape of the accessor's own DI-registration factory
    /// delegate — must pass <see cref="WorkflowTopologyRules.NoRawClientAccessorConsumptionInRepo"/>,
    /// since it never appears as a constructor parameter or a field.
    /// </summary>
    [Fact]
    public void NoRawClientAccessorConsumptionInRepo_FactoryDelegateProducesAccessor_RulePasses()
    {
        const string accessorStubSource = """
            namespace SharedKernel.Workflows.Temporal.Hosting
            {
                public interface ITemporalRawClientAccessor
                {
                }

                public class TemporalRawClientAccessor : ITemporalRawClientAccessor
                {
                }
            }
            """;

        const string factorySource = """
            namespace Fixture.RawAccessorConsumer.Factory
            {
                public class TemporalWorkflowsCompositionFactory
                {
                    public SharedKernel.Workflows.Temporal.Hosting.ITemporalRawClientAccessor CreateAccessor()
                    {
                        return new SharedKernel.Workflows.Temporal.Hosting.TemporalRawClientAccessor();
                    }
                }
            }
            """;

        var accessorStubAssembly = CompileInMemory(
            "Fixture.RawAccessorFactoryTest.Accessor",
            accessorStubSource);
        var factoryAssembly = CompileInMemory(
            "CleanRawAccessorFactory",
            factorySource,
            extraReferences: new[] { accessorStubAssembly });

        var conditionList = WorkflowTopologyRules.NoRawClientAccessorConsumptionInRepo(factoryAssembly);
        var result = conditionList.GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "TemporalWorkflowsCompositionFactory only PRODUCES the accessor via a method return value, never consumes it as a ctor parameter or field");
    }

    // ---------------------------------------------------------------------------
    // T-249 — Fire path: a contrived 17.Workflows-shaped fixture references a stubbed
    // Microsoft.Extensions.Diagnostics.HealthChecks type
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-249: When a contrived <c>SharedKernel.Workflows.Temporal</c>-shaped assembly references a
    /// type whose declaring assembly simulates
    /// <c>Microsoft.Extensions.Diagnostics.HealthChecks</c>,
    /// <see cref="WorkflowTopologyRules.NoHealthChecksDependencyInWorkflows"/> must fail.
    /// </summary>
    [Fact]
    public void NoHealthChecksDependencyInWorkflows_HealthChecksDependency_RuleFails()
    {
        const string healthChecksStubSource = """
            namespace Microsoft.Extensions.Diagnostics.HealthChecks
            {
                public interface IHealthCheck { }
            }
            """;

        const string workflowsSource = """
            namespace SharedKernel.Workflows.Temporal.Health
            {
                public class LeakyWorkflowHealthCheck : Microsoft.Extensions.Diagnostics.HealthChecks.IHealthCheck
                {
                }
            }
            """;

        var healthChecksStubAssembly = CompileInMemory(
            "Fixture.WorkflowsHealthChecksTest.Microsoft.Extensions.Diagnostics.HealthChecks",
            healthChecksStubSource);
        var workflowsAssembly = CompileInMemory(
            "ViolatingSharedKernel.Workflows.Temporal.HealthChecks",
            workflowsSource,
            extraReferences: new[] { healthChecksStubAssembly });

        var conditionList = WorkflowTopologyRules.NoHealthChecksDependencyInWorkflows(workflowsAssembly);
        var result = conditionList.GetResult();

        result.IsSuccessful.Should().BeFalse(
            because: "LeakyWorkflowHealthCheck implements Microsoft.Extensions.Diagnostics.HealthChecks.IHealthCheck directly");
    }

    // ---------------------------------------------------------------------------
    // T-250 — Pass path: a contrived fixture references another Microsoft.Extensions.* namespace
    // but not HealthChecks
    // ---------------------------------------------------------------------------

    /// <summary>
    /// T-250: A contrived <c>SharedKernel.Workflows.Temporal</c>-shaped assembly referencing
    /// <c>Microsoft.Extensions.DependencyInjection</c> (a legitimately-needed hosting dependency)
    /// but not <c>Microsoft.Extensions.Diagnostics.HealthChecks</c> must pass
    /// <see cref="WorkflowTopologyRules.NoHealthChecksDependencyInWorkflows"/>.
    /// </summary>
    [Fact]
    public void NoHealthChecksDependencyInWorkflows_OtherMicrosoftExtensionsDependency_RulePasses()
    {
        const string diStubSource = """
            namespace Microsoft.Extensions.DependencyInjection
            {
                public interface IServiceCollection { }
            }
            """;

        const string workflowsSource = """
            namespace SharedKernel.Workflows.Temporal.Hosting
            {
                public class TemporalWorkflowsServiceCollectionExtensions
                {
                    public void Register(Microsoft.Extensions.DependencyInjection.IServiceCollection services)
                    {
                    }
                }
            }
            """;

        var diStubAssembly = CompileInMemory(
            "Fixture.WorkflowsHealthChecksCleanTest.Microsoft.Extensions.DependencyInjection",
            diStubSource);
        var workflowsAssembly = CompileInMemory(
            "CleanSharedKernel.Workflows.Temporal.HealthChecks",
            workflowsSource,
            extraReferences: new[] { diStubAssembly });

        var conditionList = WorkflowTopologyRules.NoHealthChecksDependencyInWorkflows(workflowsAssembly);
        var result = conditionList.GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "TemporalWorkflowsServiceCollectionExtensions depends on DependencyInjection, not HealthChecks");
    }

    // ---------------------------------------------------------------------------
    // Real-assembly verification — 17.Workflows reached Published (all six phases ●) before this
    // phase's implementation session; the contrived fixtures above remain the primary red/green
    // proof per the phase spec, but the real assembly is ADDITIONALLY verified here since the
    // phase's own acceptance criteria treat this as GATING.
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Real-assembly verification: no type in the actual shipped <c>SharedKernel.Workflows.Temporal</c>
    /// assembly consumes <c>ITemporalRawClientAccessor</c> as a constructor parameter or field — the
    /// accessor is only ever PRODUCED by the composition-root factory wiring, never CONSUMED.
    /// </summary>
    [Fact]
    public void NoRawClientAccessorConsumptionInRepo_RealWorkflowsAssembly_RulePasses()
    {
        var workflowsAssembly = typeof(SharedKernel.Workflows.Temporal.Hosting.ITemporalRawClientAccessor).Assembly;

        var conditionList = WorkflowTopologyRules.NoRawClientAccessorConsumptionInRepo(workflowsAssembly);
        var result = conditionList.GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "the real SharedKernel.Workflows.Temporal only produces ITemporalRawClientAccessor via DI-registration factory wiring, never consumes it as a ctor/field dependency");
    }

    /// <summary>
    /// Real-assembly verification: the actual <c>SharedKernel.Workflows.Temporal</c> assembly has no
    /// dependency on <c>Microsoft.Extensions.Diagnostics.HealthChecks</c>.
    /// </summary>
    [Fact]
    public void NoHealthChecksDependencyInWorkflows_RealWorkflowsAssembly_RulePasses()
    {
        var workflowsAssembly = typeof(SharedKernel.Workflows.Temporal.Hosting.ITemporalRawClientAccessor).Assembly;

        var conditionList = WorkflowTopologyRules.NoHealthChecksDependencyInWorkflows(workflowsAssembly);
        var result = conditionList.GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "17.Workflows ships no IHealthCheck implementation and references Microsoft.Extensions.Diagnostics.HealthChecks nowhere — ProbeAsync is the sanctioned readiness primitive");
    }

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Compiles <paramref name="source"/> into an in-memory assembly and loads it for reflection.
    /// </summary>
    /// <remarks>
    /// References supplied via <paramref name="extraReferences"/> are turned into
    /// <see cref="MetadataReference"/>s from their in-memory image
    /// (<see cref="MetadataReference.CreateFromImage(System.Collections.Immutable.ImmutableArray{byte})"/>)
    /// rather than from <see cref="Assembly.Location"/> — the same technique documented for
    /// <c>RedisTopologyRulesTests</c>/<c>StorageTopologyRulesTests</c>/<c>SearchTopologyRulesTests</c>/
    /// <c>IntelligenceTopologyRulesTests</c>, avoiding CS0234 failures when chaining fixtures compiled
    /// in the same test run.
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
        };

        if (extraReferences is not null)
        {
            foreach (var extraReference in extraReferences)
            {
                references.Add(
                    MetadataReference.CreateFromImage(
                        System.Collections.Immutable.ImmutableArray.Create(
                            File.ReadAllBytes(extraReference.Location))));
            }
        }

        var compilation = CSharpCompilation.Create(
            assemblyName,
            syntaxTrees: new[] { syntaxTree },
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );

        var tempPath = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"{assemblyName}_{System.Guid.NewGuid():N}.dll");

        using (var stream = new MemoryStream())
        {
            var emitResult = compilation.Emit(stream);
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

            stream.Seek(0, SeekOrigin.Begin);
            File.WriteAllBytes(tempPath, stream.ToArray());
        }

        return Assembly.LoadFrom(tempPath);
    }
}

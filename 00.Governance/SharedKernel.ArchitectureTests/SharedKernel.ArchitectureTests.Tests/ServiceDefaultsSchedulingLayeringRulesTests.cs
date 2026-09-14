using System.Reflection;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SharedKernel.ArchitectureTests.Rules;
using Xunit;

namespace SharedKernel.ArchitectureTests.Tests;

/// <summary>
/// Tests for <see cref="ServiceDefaultsSchedulingLayeringRules"/> — the mechanical lock on the
/// <c>13.ServiceDefaults</c>→<c>19.Scheduling</c> layering grant (WO-073/P-466), added as a
/// coordinator-directed follow-up once <c>13.ServiceDefaults</c> shipped
/// <c>AddSchedulerReadinessCheck()</c> and took the real <c>ProjectReference</c> the grant permits.
/// </summary>
/// <remarks>
/// <para>
/// T-fire-1: a contrived fixture directly referencing a forbidden <c>SharedKernel.Scheduling</c>
/// type (<c>IScheduledJobRegistry</c>) via a constructor parameter must fail.
/// </para>
/// <para>
/// T-fire-2: the same forbidden reference, but reachable ONLY from inside an <c>async</c> method's
/// compiler-generated state-machine nested type (never from the outer type's own members), must
/// still fail — this is the load-bearing proof that the predicate's recursive nested-type walk is
/// doing real work, not merely defensive coverage, since the real sanctioned consumption site
/// (<c>SchedulerReadinessHealthCheck.CheckHealthAsync</c>) is itself <c>async</c>.
/// </para>
/// <para>
/// T-pass-1: a contrived fixture using ONLY the two permitted probe types must pass.
/// </para>
/// <para>
/// T-pass-2 (real-assembly): the actual, currently-shipped <c>SharedKernel.ServiceDefaults</c>
/// assembly must pass — proving the rule holds against the real, shipped
/// <c>AddSchedulerReadinessCheck()</c>/<c>SchedulerReadinessHealthCheck</c> implementation with
/// zero false positives.
/// </para>
/// </remarks>
public class ServiceDefaultsSchedulingLayeringRulesTests
{
    // ---------------------------------------------------------------------------
    // T-fire-1 — Fire path: direct field/constructor reference to a forbidden type
    // ---------------------------------------------------------------------------

    /// <summary>
    /// A contrived assembly shaped like <c>SharedKernel.ServiceDefaults</c> whose type directly
    /// references <c>SharedKernel.Scheduling.Registry.IScheduledJobRegistry</c> — a type the
    /// WO-073/P-466 grant never permits — must fail
    /// <see cref="ServiceDefaultsSchedulingLayeringRules.OnlyReachesSchedulerProbeTypes"/>.
    /// </summary>
    [Fact]
    public void OnlyReachesSchedulerProbeTypes_DirectForbiddenTypeReference_RuleFails()
    {
        const string schedulingStubSource = """
            namespace SharedKernel.Scheduling.Probes
            {
                public interface ISchedulerServiceProbe { }

                public sealed class SchedulerServiceHealth { }
            }

            namespace SharedKernel.Scheduling.Registry
            {
                public interface IScheduledJobRegistry { }
            }
            """;

        const string serviceDefaultsSource = """
            namespace SharedKernel.ServiceDefaults.HealthChecks
            {
                using SharedKernel.Scheduling.Registry;

                public class RogueSchedulerConsumer
                {
                    // Violation: SharedKernel.ServiceDefaults reaching directly into
                    // IScheduledJobRegistry — never permitted by the WO-073/P-466 grant, which
                    // scopes exclusively to ISchedulerServiceProbe/SchedulerServiceHealth.
                    private readonly IScheduledJobRegistry _registry;

                    public RogueSchedulerConsumer(IScheduledJobRegistry registry)
                    {
                        _registry = registry;
                    }
                }
            }
            """;

        var schedulingAssembly = CompileInMemory(
            "Fixture.SDSchedulingGrant.SharedKernel.Scheduling.Stub",
            schedulingStubSource);

        var serviceDefaultsAssembly = CompileInMemory(
            "Fixture.SDSchedulingGrant.ViolatingServiceDefaults",
            serviceDefaultsSource,
            extraReferences: new[] { schedulingAssembly });

        var result = ServiceDefaultsSchedulingLayeringRules
            .OnlyReachesSchedulerProbeTypes(serviceDefaultsAssembly)
            .GetResult();

        result.IsSuccessful.Should().BeFalse(
            because: "RogueSchedulerConsumer depends on SharedKernel.Scheduling.Registry." +
                     "IScheduledJobRegistry — a type never permitted by the WO-073/P-466 grant");
    }

    // ---------------------------------------------------------------------------
    // T-fire-2 — Fire path: forbidden reference reachable only from inside an async
    // state-machine nested type
    // ---------------------------------------------------------------------------

    /// <summary>
    /// A contrived assembly whose ONLY reference to
    /// <c>SharedKernel.Scheduling.Registry.IScheduledJobRegistry</c> lives inside an
    /// <c>async</c> method's compiler-generated state-machine — never in the outer type's own
    /// fields/method signatures — must still fail
    /// <see cref="ServiceDefaultsSchedulingLayeringRules.OnlyReachesSchedulerProbeTypes"/>. Proves
    /// the predicate's recursive descent into <c>TypeDefinition.NestedTypes</c> is load-bearing.
    /// </summary>
    [Fact]
    public async Task OnlyReachesSchedulerProbeTypes_ForbiddenReferenceInsideAsyncStateMachine_RuleFails()
    {
        const string schedulingStubSource = """
            namespace SharedKernel.Scheduling.Probes
            {
                public interface ISchedulerServiceProbe { }

                public sealed class SchedulerServiceHealth { }
            }

            namespace SharedKernel.Scheduling.Registry
            {
                public interface IScheduledJobRegistry
                {
                    System.Threading.Tasks.Task<bool> IsReadyAsync();
                }
            }
            """;

        const string serviceDefaultsSource = """
            namespace SharedKernel.ServiceDefaults.HealthChecks
            {
                using System.Threading.Tasks;
                using SharedKernel.Scheduling.Registry;

                // Neither CheckAsync's nor GetCandidate's own signature mentions
                // IScheduledJobRegistry anywhere — the only reference lives inside CheckAsync's
                // compiler-generated async state machine (an `isinst` pattern-match plus the
                // resulting local variable), which becomes a NESTED TYPE of this class.
                public class HiddenAsyncConsumer
                {
                    public async Task<bool> CheckAsync()
                    {
                        object candidate = GetCandidate();

                        if (candidate is IScheduledJobRegistry registry)
                        {
                            return await registry.IsReadyAsync();
                        }

                        return false;
                    }

                    private static object GetCandidate() => new object();
                }
            }
            """;

        var schedulingAssembly = CompileInMemory(
            "Fixture.SDSchedulingGrant.AsyncHidden.SharedKernel.Scheduling.Stub",
            schedulingStubSource);

        var serviceDefaultsAssembly = CompileInMemory(
            "Fixture.SDSchedulingGrant.AsyncHiddenViolatingServiceDefaults",
            serviceDefaultsSource,
            extraReferences: new[] { schedulingAssembly });

        var result = ServiceDefaultsSchedulingLayeringRules
            .OnlyReachesSchedulerProbeTypes(serviceDefaultsAssembly)
            .GetResult();

        result.IsSuccessful.Should().BeFalse(
            because: "HiddenAsyncConsumer's compiler-generated async state machine references " +
                     "IScheduledJobRegistry via an isinst pattern match — a violation the " +
                     "predicate must catch by recursing into nested types, not only the outer " +
                     "type's own members");

        // Sanity: prove the fixture source itself is genuinely valid, awaitable IL (not merely a
        // syntactically-async-shaped no-op) — instantiate and invoke it in-process. If this ever
        // stops compiling/running, the fixture no longer proves what this test claims.
        var consumerType = serviceDefaultsAssembly.GetType(
            "SharedKernel.ServiceDefaults.HealthChecks.HiddenAsyncConsumer",
            throwOnError: true)!;
        var consumerInstance = Activator.CreateInstance(consumerType)!;
        var checkAsyncMethod = consumerType.GetMethod("CheckAsync")!;
        var task = (Task<bool>)checkAsyncMethod.Invoke(consumerInstance, null)!;
        var checkResult = await task;
        checkResult.Should().BeFalse(
            because: "GetCandidate() returns a plain object, never an IScheduledJobRegistry, so " +
                     "the pattern match never matches at runtime — this only proves the fixture " +
                     "is genuinely executable IL, not that the type check passes");
    }

    // ---------------------------------------------------------------------------
    // T-pass-1 — Pass path: only the two permitted probe types are used
    // ---------------------------------------------------------------------------

    /// <summary>
    /// A contrived assembly shaped exactly like the real, sanctioned
    /// <c>SchedulerReadinessHealthCheck</c> — consuming ONLY
    /// <c>ISchedulerServiceProbe</c>/<c>SchedulerServiceHealth</c> — must pass
    /// <see cref="ServiceDefaultsSchedulingLayeringRules.OnlyReachesSchedulerProbeTypes"/>.
    /// </summary>
    [Fact]
    public void OnlyReachesSchedulerProbeTypes_OnlyPermittedProbeTypes_RulePasses()
    {
        const string schedulingStubSource = """
            namespace SharedKernel.Scheduling.Probes
            {
                public interface ISchedulerServiceProbe
                {
                    System.Threading.Tasks.Task<SchedulerServiceHealth> ProbeAsync();
                }

                public sealed class SchedulerServiceHealth
                {
                    public bool IsRunning { get; set; }
                }
            }
            """;

        const string serviceDefaultsSource = """
            namespace SharedKernel.ServiceDefaults.HealthChecks
            {
                using System.Threading.Tasks;
                using SharedKernel.Scheduling.Probes;

                public class CompliantSchedulerConsumer
                {
                    private readonly ISchedulerServiceProbe _probe;

                    public CompliantSchedulerConsumer(ISchedulerServiceProbe probe)
                    {
                        _probe = probe;
                    }

                    public async Task<bool> CheckAsync()
                    {
                        SchedulerServiceHealth health = await _probe.ProbeAsync();
                        return health.IsRunning;
                    }
                }
            }
            """;

        var schedulingAssembly = CompileInMemory(
            "Fixture.SDSchedulingGrant.Compliant.SharedKernel.Scheduling.Stub",
            schedulingStubSource);

        var serviceDefaultsAssembly = CompileInMemory(
            "Fixture.SDSchedulingGrant.CompliantServiceDefaults",
            serviceDefaultsSource,
            extraReferences: new[] { schedulingAssembly });

        var result = ServiceDefaultsSchedulingLayeringRules
            .OnlyReachesSchedulerProbeTypes(serviceDefaultsAssembly)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "CompliantSchedulerConsumer references only ISchedulerServiceProbe/" +
                     "SchedulerServiceHealth — including from inside its own async state " +
                     "machine — exactly the shape the WO-073/P-466 grant permits");
    }

    // ---------------------------------------------------------------------------
    // T-pass-2 — Pass path: the real assembly holding the P-466 grant
    // ---------------------------------------------------------------------------

    /// <summary>
    /// The real, shipped assembly that holds the P-466 13-to-19 layering grant — since WO-084,
    /// <c>SharedKernel.ServiceDefaults.Scheduling</c>, not the composition base — whose
    /// <c>SchedulerReadinessHealthCheck</c>/<c>SchedulerReadinessHealthCheckExtensions</c> consume
    /// only <c>ISchedulerServiceProbe</c>/<c>SchedulerServiceHealth</c> from
    /// <c>SharedKernel.Scheduling</c> — must pass
    /// <see cref="ServiceDefaultsSchedulingLayeringRules.OnlyReachesSchedulerProbeTypes"/> with zero
    /// violations.
    /// </summary>
    /// <remarks>
    /// Guarded against passing vacuously, for the same reason as the workflow rule's real-assembly
    /// test: pointed at an assembly that never reaches <c>SharedKernel.Scheduling</c>, the rule passes
    /// while inspecting nothing.
    /// </remarks>
    [Fact]
    public void OnlyReachesSchedulerProbeTypes_RealServiceDefaultsAssembly_RulePasses()
    {
        var serviceDefaultsAssembly =
            typeof(SharedKernel.ServiceDefaults.HealthChecks.SchedulerReadinessHealthCheckExtensions).Assembly;

        serviceDefaultsAssembly.GetName().Name.Should().Be(
            "SharedKernel.ServiceDefaults.Scheduling",
            because: "WO-084 moved the scheduler readiness check, and with it the P-466 grant, out of the base");
        serviceDefaultsAssembly.GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .Should().Contain(
                "SharedKernel.Scheduling",
                because: "a rule over an assembly that never reaches the granted package would pass vacuously");

        var result = ServiceDefaultsSchedulingLayeringRules
            .OnlyReachesSchedulerProbeTypes(serviceDefaultsAssembly)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "the real SharedKernel.ServiceDefaults.Scheduling assembly reaches into " +
                     "SharedKernel.Scheduling only through ISchedulerServiceProbe/" +
                     "SchedulerServiceHealth (SchedulerReadinessHealthCheck/Extensions), exactly " +
                     "the scope the WO-073/P-466 grant permits");
    }

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Compiles <paramref name="source"/> into an in-memory assembly for NetArchTest scanning.
    /// Follows the pattern established in <c>CommunicationLayeringRulesTests</c>/
    /// <c>PresentationLayeringRulesTests</c>: extra references are provided via
    /// <c>MetadataReference.CreateFromImage</c> from the in-memory bytes to avoid <c>CS0234</c>
    /// failures when chaining fixture assemblies.
    /// </summary>
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
            MetadataReference.CreateFromFile(Assembly.Load("System.Threading.Tasks").Location),
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

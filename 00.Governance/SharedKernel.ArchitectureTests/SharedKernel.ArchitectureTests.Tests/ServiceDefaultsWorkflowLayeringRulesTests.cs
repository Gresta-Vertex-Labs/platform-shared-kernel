using System.Reflection;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SharedKernel.ArchitectureTests.Rules;
using Xunit;

namespace SharedKernel.ArchitectureTests.Tests;

/// <summary>
/// Tests for <see cref="ServiceDefaultsWorkflowLayeringRules"/> — the mechanical lock on the
/// <c>13.ServiceDefaults</c>→<c>17.Workflows</c> layering grant (WO-047/P-291), closing the gap
/// P-490/WO-080 identified: the older of the platform's two probe-only layering grants was the
/// unenforced one, unlike the sibling <c>19.Scheduling</c> grant already locked by
/// <see cref="ServiceDefaultsSchedulingLayeringRulesTests"/>.
/// </summary>
/// <remarks>
/// <para>
/// T-fire-1: a contrived fixture directly referencing a forbidden <c>SharedKernel.Workflows.Temporal</c>
/// type (<c>ITemporalClient</c>) via a constructor parameter must fail.
/// </para>
/// <para>
/// T-fire-2: the same forbidden reference, but reachable ONLY from inside an <c>async</c> method's
/// compiler-generated state-machine nested type (never from the outer type's own members), must
/// still fail — this is the load-bearing proof that the predicate's recursive nested-type walk is
/// doing real work, not merely defensive coverage, since the real sanctioned consumption site
/// (<c>WorkflowReadinessHealthCheck.CheckHealthAsync</c>) is itself <c>async</c>.
/// </para>
/// <para>
/// T-pass-1: a contrived fixture using ONLY the two permitted probe types must pass.
/// </para>
/// <para>
/// T-pass-2 (real-assembly): the actual, currently-shipped <c>SharedKernel.ServiceDefaults</c>
/// assembly must pass — proving the rule holds against the real, shipped
/// <c>AddWorkflowReadinessCheck()</c>/<c>WorkflowReadinessHealthCheck</c> implementation with zero
/// false positives.
/// </para>
/// </remarks>
public class ServiceDefaultsWorkflowLayeringRulesTests
{
    // ---------------------------------------------------------------------------
    // T-fire-1 — Fire path: direct field/constructor reference to a forbidden type
    // ---------------------------------------------------------------------------

    /// <summary>
    /// A contrived assembly shaped like <c>SharedKernel.ServiceDefaults</c> whose type directly
    /// references <c>SharedKernel.Workflows.Temporal.Client.ITemporalClient</c> — a type the
    /// WO-047/P-291 grant never permits — must fail
    /// <see cref="ServiceDefaultsWorkflowLayeringRules.OnlyReachesWorkflowProbeTypes"/>.
    /// </summary>
    [Fact]
    public void OnlyReachesWorkflowProbeTypes_DirectForbiddenTypeReference_RuleFails()
    {
        const string workflowsStubSource = """
            namespace SharedKernel.Workflows.Temporal.Health
            {
                public interface IWorkflowServiceProbe { }

                public sealed class WorkflowServiceHealth { }
            }

            namespace SharedKernel.Workflows.Temporal.Client
            {
                public interface ITemporalClient { }
            }
            """;

        const string serviceDefaultsSource = """
            namespace SharedKernel.ServiceDefaults.HealthChecks
            {
                using SharedKernel.Workflows.Temporal.Client;

                public class RogueTemporalConsumer
                {
                    // Violation: SharedKernel.ServiceDefaults reaching directly into
                    // ITemporalClient — never permitted by the WO-047/P-291 grant, which scopes
                    // exclusively to IWorkflowServiceProbe/WorkflowServiceHealth.
                    private readonly ITemporalClient _client;

                    public RogueTemporalConsumer(ITemporalClient client)
                    {
                        _client = client;
                    }
                }
            }
            """;

        var workflowsAssembly = CompileInMemory(
            "Fixture.SDWorkflowGrant.SharedKernel.Workflows.Stub",
            workflowsStubSource);

        var serviceDefaultsAssembly = CompileInMemory(
            "Fixture.SDWorkflowGrant.ViolatingServiceDefaults",
            serviceDefaultsSource,
            extraReferences: new[] { workflowsAssembly });

        var result = ServiceDefaultsWorkflowLayeringRules
            .OnlyReachesWorkflowProbeTypes(serviceDefaultsAssembly)
            .GetResult();

        result.IsSuccessful.Should().BeFalse(
            because: "RogueTemporalConsumer depends on SharedKernel.Workflows.Temporal.Client." +
                     "ITemporalClient — a type never permitted by the WO-047/P-291 grant");
    }

    // ---------------------------------------------------------------------------
    // T-fire-2 — Fire path: forbidden reference reachable only from inside an async
    // state-machine nested type
    // ---------------------------------------------------------------------------

    /// <summary>
    /// A contrived assembly whose ONLY reference to
    /// <c>SharedKernel.Workflows.Temporal.Client.ITemporalClient</c> lives inside an <c>async</c>
    /// method's compiler-generated state-machine — never in the outer type's own fields/method
    /// signatures — must still fail
    /// <see cref="ServiceDefaultsWorkflowLayeringRules.OnlyReachesWorkflowProbeTypes"/>. Proves the
    /// predicate's recursive descent into <c>TypeDefinition.NestedTypes</c> is load-bearing.
    /// </summary>
    [Fact]
    public async Task OnlyReachesWorkflowProbeTypes_ForbiddenReferenceInsideAsyncStateMachine_RuleFails()
    {
        const string workflowsStubSource = """
            namespace SharedKernel.Workflows.Temporal.Health
            {
                public interface IWorkflowServiceProbe { }

                public sealed class WorkflowServiceHealth { }
            }

            namespace SharedKernel.Workflows.Temporal.Client
            {
                public interface ITemporalClient
                {
                    System.Threading.Tasks.Task<bool> IsConnectedAsync();
                }
            }
            """;

        const string serviceDefaultsSource = """
            namespace SharedKernel.ServiceDefaults.HealthChecks
            {
                using System.Threading.Tasks;
                using SharedKernel.Workflows.Temporal.Client;

                // Neither CheckAsync's nor GetCandidate's own signature mentions ITemporalClient
                // anywhere — the only reference lives inside CheckAsync's compiler-generated
                // async state machine (an `isinst` pattern-match plus the resulting local
                // variable), which becomes a NESTED TYPE of this class.
                public class HiddenAsyncConsumer
                {
                    public async Task<bool> CheckAsync()
                    {
                        object candidate = GetCandidate();

                        if (candidate is ITemporalClient client)
                        {
                            return await client.IsConnectedAsync();
                        }

                        return false;
                    }

                    private static object GetCandidate() => new object();
                }
            }
            """;

        var workflowsAssembly = CompileInMemory(
            "Fixture.SDWorkflowGrant.AsyncHidden.SharedKernel.Workflows.Stub",
            workflowsStubSource);

        var serviceDefaultsAssembly = CompileInMemory(
            "Fixture.SDWorkflowGrant.AsyncHiddenViolatingServiceDefaults",
            serviceDefaultsSource,
            extraReferences: new[] { workflowsAssembly });

        var result = ServiceDefaultsWorkflowLayeringRules
            .OnlyReachesWorkflowProbeTypes(serviceDefaultsAssembly)
            .GetResult();

        result.IsSuccessful.Should().BeFalse(
            because: "HiddenAsyncConsumer's compiler-generated async state machine references " +
                     "ITemporalClient via an isinst pattern match — a violation the predicate " +
                     "must catch by recursing into nested types, not only the outer type's own " +
                     "members");

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
            because: "GetCandidate() returns a plain object, never an ITemporalClient, so the " +
                     "pattern match never matches at runtime — this only proves the fixture is " +
                     "genuinely executable IL, not that the type check passes");
    }

    // ---------------------------------------------------------------------------
    // T-pass-1 — Pass path: only the two permitted probe types are used
    // ---------------------------------------------------------------------------

    /// <summary>
    /// A contrived assembly shaped exactly like the real, sanctioned
    /// <c>WorkflowReadinessHealthCheck</c> — consuming ONLY
    /// <c>IWorkflowServiceProbe</c>/<c>WorkflowServiceHealth</c> — must pass
    /// <see cref="ServiceDefaultsWorkflowLayeringRules.OnlyReachesWorkflowProbeTypes"/>.
    /// </summary>
    [Fact]
    public void OnlyReachesWorkflowProbeTypes_OnlyPermittedProbeTypes_RulePasses()
    {
        const string workflowsStubSource = """
            namespace SharedKernel.Workflows.Temporal.Health
            {
                public interface IWorkflowServiceProbe
                {
                    System.Threading.Tasks.Task<WorkflowServiceHealth> ProbeAsync();
                }

                public sealed class WorkflowServiceHealth
                {
                    public bool Reachable { get; set; }
                }
            }
            """;

        const string serviceDefaultsSource = """
            namespace SharedKernel.ServiceDefaults.HealthChecks
            {
                using System.Threading.Tasks;
                using SharedKernel.Workflows.Temporal.Health;

                public class CompliantWorkflowConsumer
                {
                    private readonly IWorkflowServiceProbe _probe;

                    public CompliantWorkflowConsumer(IWorkflowServiceProbe probe)
                    {
                        _probe = probe;
                    }

                    public async Task<bool> CheckAsync()
                    {
                        WorkflowServiceHealth health = await _probe.ProbeAsync();
                        return health.Reachable;
                    }
                }
            }
            """;

        var workflowsAssembly = CompileInMemory(
            "Fixture.SDWorkflowGrant.Compliant.SharedKernel.Workflows.Stub",
            workflowsStubSource);

        var serviceDefaultsAssembly = CompileInMemory(
            "Fixture.SDWorkflowGrant.CompliantServiceDefaults",
            serviceDefaultsSource,
            extraReferences: new[] { workflowsAssembly });

        var result = ServiceDefaultsWorkflowLayeringRules
            .OnlyReachesWorkflowProbeTypes(serviceDefaultsAssembly)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "CompliantWorkflowConsumer references only IWorkflowServiceProbe/" +
                     "WorkflowServiceHealth — including from inside its own async state machine " +
                     "— exactly the shape the WO-047/P-291 grant permits");
    }

    // ---------------------------------------------------------------------------
    // T-pass-2 — Pass path: the real assembly holding the WO-047 grant
    // ---------------------------------------------------------------------------

    /// <summary>
    /// The real, shipped assembly that holds the WO-047 13-to-17 layering grant — since WO-084,
    /// <c>SharedKernel.ServiceDefaults.Workflows.Temporal</c>, not the composition base — whose
    /// <c>WorkflowReadinessHealthCheck</c>/<c>WorkflowReadinessHealthCheckExtensions</c> consume
    /// only <c>IWorkflowServiceProbe</c>/<c>WorkflowServiceHealth</c> from
    /// <c>SharedKernel.Workflows.Temporal</c> — must pass
    /// <see cref="ServiceDefaultsWorkflowLayeringRules.OnlyReachesWorkflowProbeTypes"/> with zero
    /// violations.
    /// </summary>
    /// <remarks>
    /// Guarded against passing vacuously. The rule reports success for any assembly that never touches
    /// <c>SharedKernel.Workflows.Temporal</c> — so pointed at the wrong assembly (the base, which after
    /// WO-084 references no SharedKernel package at all) it would pass while inspecting nothing. The
    /// test therefore first asserts it holds the integration assembly, and that the assembly genuinely
    /// references the package the grant covers.
    /// </remarks>
    [Fact]
    public void OnlyReachesWorkflowProbeTypes_RealServiceDefaultsAssembly_RulePasses()
    {
        var serviceDefaultsAssembly =
            typeof(SharedKernel.ServiceDefaults.HealthChecks.WorkflowReadinessHealthCheckExtensions).Assembly;

        serviceDefaultsAssembly.GetName().Name.Should().Be(
            "SharedKernel.ServiceDefaults.Workflows.Temporal",
            because: "WO-084 moved the workflow readiness check, and with it the WO-047 grant, out of the base");
        serviceDefaultsAssembly.GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .Should().Contain(
                "SharedKernel.Workflows.Temporal",
                because: "a rule over an assembly that never reaches the granted package would pass vacuously");

        var result = ServiceDefaultsWorkflowLayeringRules
            .OnlyReachesWorkflowProbeTypes(serviceDefaultsAssembly)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "the real SharedKernel.ServiceDefaults.Workflows.Temporal assembly reaches into " +
                     "SharedKernel.Workflows.Temporal only through IWorkflowServiceProbe/" +
                     "WorkflowServiceHealth (WorkflowReadinessHealthCheck/Extensions), exactly " +
                     "the scope the WO-047/P-291 grant permits");
    }

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Compiles <paramref name="source"/> into an in-memory assembly for NetArchTest scanning.
    /// Follows the pattern established in <c>ServiceDefaultsSchedulingLayeringRulesTests</c>/
    /// <c>CommunicationLayeringRulesTests</c>/<c>PresentationLayeringRulesTests</c>: extra
    /// references are provided via <c>MetadataReference.CreateFromImage</c> from the in-memory
    /// bytes to avoid <c>CS0234</c> failures when chaining fixture assemblies.
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

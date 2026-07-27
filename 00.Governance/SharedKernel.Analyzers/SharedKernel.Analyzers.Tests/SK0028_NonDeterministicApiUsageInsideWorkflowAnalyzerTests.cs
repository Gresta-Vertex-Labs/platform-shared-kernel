using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using SharedKernel.Analyzers.Diagnostics;
using Xunit;

namespace SharedKernel.Analyzers.Tests;

/// <summary>Tests for SK0028 <see cref="NonDeterministicApiUsageInsideWorkflowAnalyzer"/> — WO-046 P-290.</summary>
/// <remarks>
/// <para>
/// T-233..T-240: fire paths for all seven forbidden shapes (the DateTime/DateTimeOffset shape is
/// exercised across all four member-name combinations; Task.Run is exercised as bonus coverage
/// beyond the phase's own minimum). T-241: the single most important pass-path test in the phase —
/// all seven shapes inside an <c>ActivityBase</c>-derived type do NOT fire. T-242: the sanctioned
/// <c>Workflow.UtcNow</c>/<c>.NewGuid()</c>/<c>.Random</c> primitives do NOT fire inside a genuine
/// workflow type.
/// </para>
/// <para>
/// Fixture stubs are in-compilation stand-ins for the real <c>Temporalio.Workflows.WorkflowAttribute</c>,
/// <c>Temporalio.Activities.ActivityAttribute</c>, <c>SharedKernel.Workflows.Temporal.Authoring.WorkflowBase</c>/
/// <c>ActivityBase</c>, <c>SharedKernel.Primitives.IClock</c>, and
/// <c>Microsoft.Extensions.Logging.ILogger</c>/<c>ILogger&lt;T&gt;</c> types (none of these packages is
/// referenced by this test project) — the same in-compilation-stub technique already established by
/// SK0013/SK0020/SK0021/SK0026. Real BCL types (<c>System.DateTime</c>, <c>System.DateTimeOffset</c>,
/// <c>System.Guid</c>, <c>System.Random</c>, <c>System.Threading.Tasks.Task</c>,
/// <c>System.Environment</c>, <c>System.IO.File</c>) resolve directly from the default sandbox
/// closure with no stub needed, mirroring SK0013's real <c>HttpClient</c> precedent.
/// </para>
/// </remarks>
public class SK0028_NonDeterministicApiUsageInsideWorkflowAnalyzerTests
{
    private const string Stubs = """
        namespace Temporalio.Workflows
        {
            public class WorkflowAttribute : System.Attribute
            {
            }
        }

        namespace Temporalio.Activities
        {
            public class ActivityAttribute : System.Attribute
            {
            }
        }

        namespace SharedKernel.Workflows.Temporal.Authoring
        {
            public abstract class WorkflowBase
            {
            }

            public abstract class ActivityBase
            {
            }
        }

        namespace SharedKernel.Primitives
        {
            public interface IClock
            {
                System.DateTimeOffset UtcNow { get; }
            }
        }

        namespace Microsoft.Extensions.Logging
        {
            public interface ILogger
            {
            }

            public interface ILogger<T> : ILogger
            {
            }
        }

        """;

    private static CSharpAnalyzerTest<NonDeterministicApiUsageInsideWorkflowAnalyzer, DefaultVerifier> CreateTest(
        string source) =>
        new()
        {
            TestCode = Stubs + source,
        };

    // ---------------------------------------------------------------------------
    // T-233 — Fire path: DateTime.UtcNow / .Now / DateTimeOffset.UtcNow / .Now
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task FirePath_DateTimeUtcNow_ReportsSk0028()
    {
        var test = CreateTest(
            """
            namespace Fixture.Workflows
            {
                public class OrderWorkflow : SharedKernel.Workflows.Temporal.Authoring.WorkflowBase
                {
                    public void Run()
                    {
                        var now = {|SK0028:System.DateTime.UtcNow|};
                    }
                }
            }
            """
        );
        await test.RunAsync();
    }

    [Fact]
    public async Task FirePath_DateTimeNow_ReportsSk0028()
    {
        var test = CreateTest(
            """
            namespace Fixture.Workflows
            {
                public class OrderWorkflow : SharedKernel.Workflows.Temporal.Authoring.WorkflowBase
                {
                    public void Run()
                    {
                        var now = {|SK0028:System.DateTime.Now|};
                    }
                }
            }
            """
        );
        await test.RunAsync();
    }

    [Fact]
    public async Task FirePath_DateTimeOffsetUtcNow_ReportsSk0028()
    {
        var test = CreateTest(
            """
            namespace Fixture.Workflows
            {
                public class OrderWorkflow : SharedKernel.Workflows.Temporal.Authoring.WorkflowBase
                {
                    public void Run()
                    {
                        var now = {|SK0028:System.DateTimeOffset.UtcNow|};
                    }
                }
            }
            """
        );
        await test.RunAsync();
    }

    [Fact]
    public async Task FirePath_DateTimeOffsetNow_ReportsSk0028()
    {
        var test = CreateTest(
            """
            namespace Fixture.Workflows
            {
                public class OrderWorkflow : SharedKernel.Workflows.Temporal.Authoring.WorkflowBase
                {
                    public void Run()
                    {
                        var now = {|SK0028:System.DateTimeOffset.Now|};
                    }
                }
            }
            """
        );
        await test.RunAsync();
    }

    // ---------------------------------------------------------------------------
    // T-234 — Fire path: Guid.NewGuid()
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task FirePath_GuidNewGuid_ReportsSk0028()
    {
        var test = CreateTest(
            """
            namespace Fixture.Workflows
            {
                public class OrderWorkflow : SharedKernel.Workflows.Temporal.Authoring.WorkflowBase
                {
                    public void Run()
                    {
                        var id = {|SK0028:System.Guid.NewGuid()|};
                    }
                }
            }
            """
        );
        await test.RunAsync();
    }

    // ---------------------------------------------------------------------------
    // T-235 — Fire path: new Random()
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task FirePath_NewRandom_ReportsSk0028()
    {
        var test = CreateTest(
            """
            namespace Fixture.Workflows
            {
                public class OrderWorkflow : SharedKernel.Workflows.Temporal.Authoring.WorkflowBase
                {
                    public void Run()
                    {
                        var rnd = {|SK0028:new System.Random()|};
                    }
                }
            }
            """
        );
        await test.RunAsync();
    }

    // ---------------------------------------------------------------------------
    // T-236 — Fire path: Task.Delay(...) — plus Task.Run(...) as bonus coverage
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task FirePath_TaskDelay_ReportsSk0028()
    {
        var test = CreateTest(
            """
            namespace Fixture.Workflows
            {
                public class OrderWorkflow : SharedKernel.Workflows.Temporal.Authoring.WorkflowBase
                {
                    public async System.Threading.Tasks.Task RunAsync()
                    {
                        await {|SK0028:System.Threading.Tasks.Task.Delay(100)|};
                    }
                }
            }
            """
        );
        await test.RunAsync();
    }

    [Fact]
    public async Task FirePath_TaskRun_ReportsSk0028()
    {
        var test = CreateTest(
            """
            namespace Fixture.Workflows
            {
                public class OrderWorkflow : SharedKernel.Workflows.Temporal.Authoring.WorkflowBase
                {
                    public void Run()
                    {
                        {|SK0028:System.Threading.Tasks.Task.Run(() => { })|};
                    }
                }
            }
            """
        );
        await test.RunAsync();
    }

    // ---------------------------------------------------------------------------
    // T-237 — Fire path: ConfigureAwait(false)
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task FirePath_ConfigureAwaitFalse_ReportsSk0028()
    {
        var test = CreateTest(
            """
            namespace Fixture.Workflows
            {
                public class OrderWorkflow : SharedKernel.Workflows.Temporal.Authoring.WorkflowBase
                {
                    public async System.Threading.Tasks.Task RunAsync()
                    {
                        await {|SK0028:System.Threading.Tasks.Task.CompletedTask.ConfigureAwait(false)|};
                    }
                }
            }
            """
        );
        await test.RunAsync();
    }

    /// <summary>
    /// Edge case — <c>ConfigureAwait(true)</c> is not one of the seven forbidden shapes (only the
    /// <c>false</c> argument matters, since that is the form that escapes the workflow's own
    /// deterministic synchronization context).
    /// </summary>
    [Fact]
    public async Task PassPath_ConfigureAwaitTrue_NoDiagnostic()
    {
        var test = CreateTest(
            """
            namespace Fixture.Workflows
            {
                public class OrderWorkflow : SharedKernel.Workflows.Temporal.Authoring.WorkflowBase
                {
                    public async System.Threading.Tasks.Task RunAsync()
                    {
                        await System.Threading.Tasks.Task.CompletedTask.ConfigureAwait(true);
                    }
                }
            }
            """
        );
        await test.RunAsync();
    }

    // ---------------------------------------------------------------------------
    // T-238 — Fire path: Environment.*/File.* (property and method forms)
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task FirePath_EnvironmentMachineName_ReportsSk0028()
    {
        var test = CreateTest(
            """
            namespace Fixture.Workflows
            {
                public class OrderWorkflow : SharedKernel.Workflows.Temporal.Authoring.WorkflowBase
                {
                    public void Run()
                    {
                        var machine = {|SK0028:System.Environment.MachineName|};
                    }
                }
            }
            """
        );
        await test.RunAsync();
    }

    [Fact]
    public async Task FirePath_EnvironmentGetEnvironmentVariable_ReportsSk0028()
    {
        var test = CreateTest(
            """
            namespace Fixture.Workflows
            {
                public class OrderWorkflow : SharedKernel.Workflows.Temporal.Authoring.WorkflowBase
                {
                    public void Run()
                    {
                        var value = {|SK0028:System.Environment.GetEnvironmentVariable("X")|};
                    }
                }
            }
            """
        );
        await test.RunAsync();
    }

    [Fact]
    public async Task FirePath_FileExists_ReportsSk0028()
    {
        var test = CreateTest(
            """
            namespace Fixture.Workflows
            {
                public class OrderWorkflow : SharedKernel.Workflows.Temporal.Authoring.WorkflowBase
                {
                    public void Run()
                    {
                        var exists = {|SK0028:System.IO.File.Exists("x")|};
                    }
                }
            }
            """
        );
        await test.RunAsync();
    }

    // ---------------------------------------------------------------------------
    // T-239 — Fire path: constructor-injected IClock
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task FirePath_CtorInjectedIClock_ReportsSk0028()
    {
        var test = CreateTest(
            """
            namespace Fixture.Workflows
            {
                public class OrderWorkflow : SharedKernel.Workflows.Temporal.Authoring.WorkflowBase
                {
                    public OrderWorkflow({|SK0028:SharedKernel.Primitives.IClock|} clock)
                    {
                    }
                }
            }
            """
        );
        await test.RunAsync();
    }

    // ---------------------------------------------------------------------------
    // T-240 — Fire path: constructor-injected ILogger<T>
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task FirePath_CtorInjectedIloggerOfT_ReportsSk0028()
    {
        var test = CreateTest(
            """
            namespace Fixture.Workflows
            {
                public class OrderWorkflow : SharedKernel.Workflows.Temporal.Authoring.WorkflowBase
                {
                    public OrderWorkflow({|SK0028:Microsoft.Extensions.Logging.ILogger<OrderWorkflow>|} logger)
                    {
                    }
                }
            }
            """
        );
        await test.RunAsync();
    }

    /// <summary>
    /// Bonus fire path — type attribution alone (<c>[Workflow]</c>, without extending
    /// <c>WorkflowBase</c>) is sufficient to place a type in scope.
    /// </summary>
    [Fact]
    public async Task FirePath_WorkflowAttributedTypeWithoutBaseClass_ReportsSk0028()
    {
        var test = CreateTest(
            """
            namespace Fixture.Attributed
            {
                [Temporalio.Workflows.Workflow]
                public class AttributedOnlyWorkflow
                {
                    public void Run()
                    {
                        var now = {|SK0028:System.DateTime.UtcNow|};
                    }
                }
            }
            """
        );
        await test.RunAsync();
    }

    // ---------------------------------------------------------------------------
    // T-241 — Pass path: all seven shapes inside an ActivityBase-derived type do NOT fire.
    // The single most important pass-path test in this phase.
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task PassPath_ActivityBaseDerivedType_AllSevenShapes_NoDiagnostic()
    {
        var test = CreateTest(
            """
            namespace Fixture.Activities
            {
                public class SampleActivity : SharedKernel.Workflows.Temporal.Authoring.ActivityBase
                {
                    public SampleActivity(
                        Microsoft.Extensions.Logging.ILogger<SampleActivity> logger,
                        SharedKernel.Primitives.IClock clock)
                    {
                    }

                    public async System.Threading.Tasks.Task DoWorkAsync()
                    {
                        var now = System.DateTime.UtcNow;
                        var now2 = System.DateTimeOffset.Now;
                        var id = System.Guid.NewGuid();
                        var rnd = new System.Random();
                        await System.Threading.Tasks.Task.Delay(100);
                        await System.Threading.Tasks.Task.CompletedTask.ConfigureAwait(false);
                        var machine = System.Environment.MachineName;
                        var exists = System.IO.File.Exists("x");
                    }
                }
            }
            """
        );
        await test.RunAsync();
    }

    /// <summary>
    /// Bonus pass path — an <c>[Activity]</c>-attributed type (without extending
    /// <c>ActivityBase</c>) is also excluded.
    /// </summary>
    [Fact]
    public async Task PassPath_ActivityAttributedTypeWithoutBaseClass_NoDiagnostic()
    {
        var test = CreateTest(
            """
            namespace Fixture.AttributedActivities
            {
                [Temporalio.Activities.Activity]
                public class AttributedOnlyActivity
                {
                    public void Run()
                    {
                        var now = System.DateTime.UtcNow;
                    }
                }
            }
            """
        );
        await test.RunAsync();
    }

    /// <summary>
    /// Bonus pass path — proves the Activity exclusion is checked FIRST and wins even when a type
    /// carries BOTH a <c>[Workflow]</c> attribute and an <c>[Activity]</c> attribute.
    /// </summary>
    [Fact]
    public async Task PassPath_ActivityExclusionWinsOverWorkflowAttribution_NoDiagnostic()
    {
        var test = CreateTest(
            """
            namespace Fixture.BothAttributes
            {
                [Temporalio.Workflows.Workflow]
                [Temporalio.Activities.Activity]
                public class BothAttributedType
                {
                    public void Run()
                    {
                        var now = System.DateTime.UtcNow;
                    }
                }
            }
            """
        );
        await test.RunAsync();
    }

    // ---------------------------------------------------------------------------
    // T-242 — Pass path: the sanctioned Workflow.UtcNow/.NewGuid()/.Random primitives
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task PassPath_WorkflowUsesSanctionedPrimitives_NoDiagnostic()
    {
        var test =
            CreateTest(
                """
                namespace Temporalio.Workflows
                {
                    public static class Workflow
                    {
                        public static System.DateTimeOffset UtcNow => default;

                        public static System.Guid NewGuid() => default;

                        public static object? Random => null;

                        public static System.Threading.Tasks.Task DelayAsync(System.TimeSpan delay) =>
                            System.Threading.Tasks.Task.CompletedTask;
                    }
                }

                namespace Fixture.Workflows
                {
                    public class OrderWorkflow : SharedKernel.Workflows.Temporal.Authoring.WorkflowBase
                    {
                        public async System.Threading.Tasks.Task RunAsync()
                        {
                            var now = Temporalio.Workflows.Workflow.UtcNow;
                            var id = Temporalio.Workflows.Workflow.NewGuid();
                            var rnd = Temporalio.Workflows.Workflow.Random;
                            await Temporalio.Workflows.Workflow.DelayAsync(System.TimeSpan.FromSeconds(1));
                        }
                    }
                }
                """
            );
        await test.RunAsync();
    }

    /// <summary>
    /// Bonus pass path — an ordinary type entirely unrelated to workflows (no attribute, no
    /// <c>WorkflowBase</c>/<c>ActivityBase</c> base type) may freely use every one of the seven
    /// otherwise-forbidden APIs.
    /// </summary>
    [Fact]
    public async Task PassPath_OrdinaryNonWorkflowType_NoDiagnostic()
    {
        var test = CreateTest(
            """
            namespace Fixture.Ordinary
            {
                public class OrdinaryService
                {
                    public OrdinaryService(Microsoft.Extensions.Logging.ILogger<OrdinaryService> logger, SharedKernel.Primitives.IClock clock)
                    {
                    }

                    public async System.Threading.Tasks.Task RunAsync()
                    {
                        var now = System.DateTime.UtcNow;
                        var id = System.Guid.NewGuid();
                        var rnd = new System.Random();
                        await System.Threading.Tasks.Task.Delay(100);
                        await System.Threading.Tasks.Task.CompletedTask.ConfigureAwait(false);
                        var machine = System.Environment.MachineName;
                        var exists = System.IO.File.Exists("x");
                    }
                }
            }
            """
        );
        await test.RunAsync();
    }
}

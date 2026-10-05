using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using SharedKernel.Analyzers.Diagnostics;
using Xunit;

namespace SharedKernel.Analyzers.Tests;

/// <summary>Tests for SK0029 <see cref="RawTemporalClientConstructorInjectionAnalyzer"/> — WO-046 P-290.</summary>
/// <remarks>
/// <para>
/// T-243: fire path — <c>ITemporalClient</c>/<c>TemporalWorker</c>/<c>WorkflowHandle</c> injected in
/// a constructor outside <c>SharedKernel.Workflows.Temporal</c> triggers SK0029. T-244: the
/// in-<c>SharedKernel.Workflows.Temporal</c>-namespace pass path. T-245: an unrelated user-defined
/// type sharing the simple name <c>WorkflowHandle</c> but a different namespace does not trigger.
/// </para>
/// <para>
/// Fixture stubs are in-compilation stand-ins for the real <c>Temporalio.Client.ITemporalClient</c>/
/// <c>TemporalClient</c>/<c>WorkflowHandle</c> and <c>Temporalio.Worker.TemporalWorker</c> types
/// (neither package is referenced by this test project) — the same in-compilation-stub technique
/// already established by SK0013/SK0026.
/// </para>
/// </remarks>
public class SK0029_RawTemporalClientConstructorInjectionAnalyzerTests
{
    private const string ClientStubs = """
        namespace Temporalio.Client
        {
            public interface ITemporalClient
            {
            }

            public class TemporalClient : ITemporalClient
            {
            }

            public class WorkflowHandle
            {
            }

            public class WorkflowHandle<T>
            {
            }
        }

        namespace Temporalio.Worker
        {
            public class TemporalWorker
            {
            }
        }

        """;

    private static CSharpAnalyzerTest<RawTemporalClientConstructorInjectionAnalyzer, DefaultVerifier> CreateTest(
        string source) =>
        new()
        {
            TestCode = ClientStubs + source,
        };

    // ---------------------------------------------------------------------------
    // T-243 — Fire path: raw Temporal client types injected outside SharedKernel.Workflows.Temporal
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task FirePath_ITemporalClientInjectedOutsideOwningPackage_ReportsSk0029()
    {
        var test = CreateTest(
            """
            namespace Fixture.Consumer
            {
                public class OrderWorkflowService
                {
                    public OrderWorkflowService({|SK0029:Temporalio.Client.ITemporalClient|} client)
                    {
                    }
                }
            }
            """
        );
        await test.RunAsync();
    }

    [Fact]
    public async Task FirePath_TemporalClientInjectedOutsideOwningPackage_ReportsSk0029()
    {
        var test = CreateTest(
            """
            namespace Fixture.Consumer
            {
                public class OrderWorkflowService
                {
                    public OrderWorkflowService({|SK0029:Temporalio.Client.TemporalClient|} client)
                    {
                    }
                }
            }
            """
        );
        await test.RunAsync();
    }

    [Fact]
    public async Task FirePath_TemporalWorkerInjectedOutsideOwningPackage_ReportsSk0029()
    {
        var test = CreateTest(
            """
            namespace Fixture.Consumer
            {
                public class WorkerHostedService
                {
                    public WorkerHostedService({|SK0029:Temporalio.Worker.TemporalWorker|} worker)
                    {
                    }
                }
            }
            """
        );
        await test.RunAsync();
    }

    [Fact]
    public async Task FirePath_NonGenericWorkflowHandleInjectedOutsideOwningPackage_ReportsSk0029()
    {
        var test = CreateTest(
            """
            namespace Fixture.Consumer
            {
                public class OrderWorkflowService
                {
                    public OrderWorkflowService({|SK0029:Temporalio.Client.WorkflowHandle|} handle)
                    {
                    }
                }
            }
            """
        );
        await test.RunAsync();
    }

    [Fact]
    public async Task FirePath_GenericWorkflowHandleInjectedOutsideOwningPackage_ReportsSk0029()
    {
        var test = CreateTest(
            """
            namespace Fixture.Consumer
            {
                public class OrderWorkflowService
                {
                    public OrderWorkflowService({|SK0029:Temporalio.Client.WorkflowHandle<int>|} handle)
                    {
                    }
                }
            }
            """
        );
        await test.RunAsync();
    }

    // ---------------------------------------------------------------------------
    // T-244 — Pass path: raw Temporal client types injected inside SharedKernel.Workflows.Temporal
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task PassPath_ITemporalClientInjectedInsideOwningPackage_NoDiagnostic()
    {
        var test = CreateTest(
            """
            namespace SharedKernel.Workflows.Temporal.Dispatch
            {
                public class WorkflowDispatcher
                {
                    public WorkflowDispatcher(Temporalio.Client.ITemporalClient client)
                    {
                    }
                }
            }
            """
        );
        await test.RunAsync();
    }

    [Fact]
    public async Task PassPath_TemporalWorkerInjectedInsideOwningPackage_NoDiagnostic()
    {
        var test = CreateTest(
            """
            namespace SharedKernel.Workflows.Temporal.Hosting
            {
                public class TemporalWorkflowsCompositionLogger
                {
                    public TemporalWorkflowsCompositionLogger(Temporalio.Worker.TemporalWorker worker)
                    {
                    }
                }
            }
            """
        );
        await test.RunAsync();
    }

    [Fact]
    public async Task PassPath_WorkflowHandleInjectedInsideOwningPackage_NoDiagnostic()
    {
        var test = CreateTest(
            """
            namespace SharedKernel.Workflows.Temporal.Dispatch
            {
                public class WorkflowHandleAdapter
                {
                    public WorkflowHandleAdapter(Temporalio.Client.WorkflowHandle<string> handle)
                    {
                    }
                }
            }
            """
        );
        await test.RunAsync();
    }

    // ---------------------------------------------------------------------------
    // T-245 — Pass path: an unrelated user-defined type sharing the simple name WorkflowHandle
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task PassPath_UnrelatedWorkflowHandleTypeFromDifferentNamespace_NoDiagnostic()
    {
        var test = CreateTest(
            """
            namespace Fixture.Unrelated
            {
                public class WorkflowHandle
                {
                }

                public class SomeOtherConsumer
                {
                    public SomeOtherConsumer(WorkflowHandle handle)
                    {
                    }
                }
            }
            """
        );
        await test.RunAsync();
    }
}

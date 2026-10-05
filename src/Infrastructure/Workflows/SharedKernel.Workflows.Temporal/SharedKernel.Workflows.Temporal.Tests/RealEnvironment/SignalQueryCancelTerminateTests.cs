using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Primitives.Results;
using SharedKernel.Workflows.Temporal.Dispatch;
using Temporalio.Api.Enums.V1;

namespace SharedKernel.Workflows.Temporal.Tests.RealEnvironment;

/// <summary>
/// T-11 — signal / query / cancel / terminate against a real environment: a signal observed by the
/// running workflow, a query returning live state, cancel running the compensation path to a
/// cancelled completion, terminate ending the execution <b>without</b> compensation. Asserts the two
/// are genuinely different, since that difference is the reason both are exposed.
/// </summary>
[Collection(TemporalEnvironmentCollection.Name)]
public sealed class SignalQueryCancelTerminateTests(TemporalTestFixture fixture)
{
    private static WorkflowStartOptions Options(string businessKey) => new()
    {
        TaskQueue = TemporalTestFixture.TaskQueue,
        BusinessKey = businessKey,
        IdReusePolicy = WorkflowIdReusePolicy.AllowDuplicate,
        IdConflictPolicy = WorkflowIdConflictPolicy.Fail,
    };

    [Fact]
    public async Task Signal_ObservedByRunningWorkflow_AndQuery_ReturnsLiveState()
    {
        using IServiceScope scope = fixture.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IWorkflowDispatcher>();

        Result<IWorkflowHandle> startResult = await dispatcher.StartAsync<CompensatingWorkflow, string>(
            $"signal-query-{Guid.NewGuid():N}",
            Options($"signal-query-{Guid.NewGuid():N}"),
            TenantScope.For(TestTenants.Sigqry));
        startResult.IsSuccess.Should().BeTrue();
        IWorkflowHandle handle = startResult.Value;

        Result<string> beforeSignal = await handle.QueryAsync<string>("GetMessage");
        beforeSignal.IsSuccess.Should().BeTrue();
        beforeSignal.Value.Should().BeEmpty();

        Result signalResult = await handle.SignalAsync("SetMessage", "hello-from-signal");
        signalResult.IsSuccess.Should().BeTrue();

        Result<string> afterSignal = await handle.QueryAsync<string>("GetMessage");
        afterSignal.IsSuccess.Should().BeTrue();
        afterSignal.Value.Should().Be("hello-from-signal", because: "the query must return the workflow's live, signal-updated state");
    }

    [Fact]
    public async Task Cancel_RunsCompensation_AndCompletesAsCancelled()
    {
        using IServiceScope scope = fixture.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IWorkflowDispatcher>();

        string compensationKey = $"cancel-compensation-{Guid.NewGuid():N}";
        Result<IWorkflowHandle> startResult = await dispatcher.StartAsync<CompensatingWorkflow, string>(
            compensationKey,
            Options($"cancel-{Guid.NewGuid():N}"),
            TenantScope.For(TestTenants.Sigqry));
        startResult.IsSuccess.Should().BeTrue();

        Result cancelResult = await startResult.Value.CancelAsync();
        cancelResult.IsSuccess.Should().BeTrue();

        var handle = dispatcher.GetHandle<string>(startResult.Value.WorkflowId, runId: null, TenantScope.For(TestTenants.Sigqry));
        Result<string> result = await handle.GetResultAsync();

        result.IsFailure.Should().BeTrue(because: "a cancelled workflow completes as a failure, not a success");
        CompensationActivity.Invocations.Should().ContainKey(compensationKey)
            .WhoseValue.Should().BeTrue(because: "cancellation must run the workflow's compensation path before completing");
    }

    [Fact]
    public async Task Terminate_EndsExecution_WithoutRunningCompensation()
    {
        using IServiceScope scope = fixture.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IWorkflowDispatcher>();

        string compensationKey = $"terminate-compensation-{Guid.NewGuid():N}";
        Result<IWorkflowHandle> startResult = await dispatcher.StartAsync<CompensatingWorkflow, string>(
            compensationKey,
            Options($"terminate-{Guid.NewGuid():N}"),
            TenantScope.For(TestTenants.Sigqry));
        startResult.IsSuccess.Should().BeTrue();

        Result terminateResult = await startResult.Value.TerminateAsync("test-driven termination — no compensation expected");
        terminateResult.IsSuccess.Should().BeTrue();

        var handle = dispatcher.GetHandle<string>(startResult.Value.WorkflowId, runId: null, TenantScope.For(TestTenants.Sigqry));
        Result<string> result = await handle.GetResultAsync();

        result.IsFailure.Should().BeTrue(because: "a terminated workflow completes as a failure, not a success");
        CompensationActivity.Invocations.Should().NotContainKey(compensationKey, because: "terminate kills the execution with NO chance for the workflow to react — compensation must never run");
    }

    [Fact]
    public async Task CancelAndTerminate_ProduceGenuinelyDifferentOutcomes()
    {
        using IServiceScope scope = fixture.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IWorkflowDispatcher>();

        string cancelCompensationKey = $"diff-cancel-{Guid.NewGuid():N}";
        string terminateCompensationKey = $"diff-terminate-{Guid.NewGuid():N}";

        Result<IWorkflowHandle> cancelStart = await dispatcher.StartAsync<CompensatingWorkflow, string>(
            cancelCompensationKey, Options($"diff-cancel-wf-{Guid.NewGuid():N}"), TenantScope.For(TestTenants.Sigqry));
        Result<IWorkflowHandle> terminateStart = await dispatcher.StartAsync<CompensatingWorkflow, string>(
            terminateCompensationKey, Options($"diff-terminate-wf-{Guid.NewGuid():N}"), TenantScope.For(TestTenants.Sigqry));

        await cancelStart.Value.CancelAsync();
        await terminateStart.Value.TerminateAsync("comparison termination");

        var cancelHandle = dispatcher.GetHandle<string>(cancelStart.Value.WorkflowId, runId: null, TenantScope.For(TestTenants.Sigqry));
        var terminateHandle = dispatcher.GetHandle<string>(terminateStart.Value.WorkflowId, runId: null, TenantScope.For(TestTenants.Sigqry));

        await cancelHandle.GetResultAsync();
        await terminateHandle.GetResultAsync();

        CompensationActivity.Invocations.Should().ContainKey(cancelCompensationKey).WhoseValue.Should().BeTrue();
        CompensationActivity.Invocations.Should().NotContainKey(terminateCompensationKey);
    }
}

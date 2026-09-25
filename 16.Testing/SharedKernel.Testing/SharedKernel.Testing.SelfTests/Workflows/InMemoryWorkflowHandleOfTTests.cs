using SharedKernel.Execution.Tenancy;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;
using SharedKernel.Testing.Workflows;
using SharedKernel.Workflows.Temporal.Dispatch;
using SharedKernel.Workflows.Temporal.Errors;

namespace SharedKernel.Testing.SelfTests.Workflows;

/// <summary>
/// Proves <see cref="InMemoryWorkflowHandle{TResult}"/> -- in particular
/// <see cref="InMemoryWorkflowHandle{TResult}.GetResultAsync"/>'s deterministic, never-polling
/// contract, and that every other member forwards verbatim to the composed
/// <see cref="InMemoryWorkflowHandle"/> -- no consuming domain has adopted this fake yet, so this
/// self-test is the only behavioral proof today, per the SelfTests routing rule (T-55/P-288/WO-046).
/// </summary>
public sealed class InMemoryWorkflowHandleOfTTests
{
    private static async Task<InMemoryWorkflowHandle<TResult>> StartedHandleAsync<TResult>(
        InMemoryWorkflowDispatcher dispatcher, string businessKey = "k1")
    {
        Result<IWorkflowHandle<TResult>> result = await dispatcher.StartAsync<SampleWorkflow, string, TResult>(
            "input", WorkflowsTestFixtures.ValidOptions(businessKey), TenantScope.For(WorkflowsTestFixtures.TenantA));
        Assert.True(result.IsSuccess);
        return (InMemoryWorkflowHandle<TResult>)result.Value;
    }

    // --- GetResultAsync: deterministic no-poll contract ---

    [Fact]
    public async Task GetResultAsync_BeforeCompleteOrFail_ThrowsImmediately_NamingTheWorkflowId()
    {
        var dispatcher = new InMemoryWorkflowDispatcher();
        var handle = await StartedHandleAsync<string>(dispatcher);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => handle.GetResultAsync());

        Assert.Contains(handle.WorkflowId, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetResultAsync_AfterCompleteWorkflow_ReturnsImmediately_WithConfiguredValue()
    {
        var dispatcher = new InMemoryWorkflowDispatcher();
        var handle = await StartedHandleAsync<string>(dispatcher);

        dispatcher.CompleteWorkflow(handle.WorkflowId, "final-result");
        Result<string> result = await handle.GetResultAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal("final-result", result.Value);
    }

    [Fact]
    public async Task GetResultAsync_AfterFailWorkflow_ReturnsFailure_WithConfiguredError()
    {
        var dispatcher = new InMemoryWorkflowDispatcher();
        var handle = await StartedHandleAsync<string>(dispatcher);
        var error = Error.Unexpected("workflow.boom", "it broke");

        dispatcher.FailWorkflow(handle.WorkflowId, error);
        Result<string> result = await handle.GetResultAsync();

        Assert.True(result.IsFailure);
        Assert.Equal(error, result.Error);
    }

    [Fact]
    public async Task GetResultAsync_NoBackingExecution_ReturnsNotFound_NeverThrows()
    {
        var dispatcher = new InMemoryWorkflowDispatcher();
        IWorkflowHandle<string> handle = dispatcher.GetHandle<string>("no-such-id", runId: null, TenantScope.For(WorkflowsTestFixtures.TenantA));

        Result<string> result = await handle.GetResultAsync();

        Assert.True(result.IsFailure);
        Assert.Equal(WorkflowErrors.NotFound("no-such-id"), result.Error);
    }

    // --- forwarding surface ---

    [Fact]
    public async Task WorkflowIdAndRunId_ForwardToInnerHandle()
    {
        var dispatcher = new InMemoryWorkflowDispatcher();
        var handle = await StartedHandleAsync<string>(dispatcher);

        Assert.False(string.IsNullOrWhiteSpace(handle.WorkflowId));
        Assert.False(string.IsNullOrWhiteSpace(handle.RunId));
    }

    [Fact]
    public async Task SignalAsync_ForwardsToInnerHandle_AndIsObservableViaShouldHaveSignalled()
    {
        var dispatcher = new InMemoryWorkflowDispatcher();
        var handle = await StartedHandleAsync<string>(dispatcher);

        await handle.SignalAsync("go", "payload");

        Assert.Equal("payload", handle.ShouldHaveSignalled("go"));
        Assert.Contains(("go", (object?)"payload"), handle.SignalsReceived);
    }

    [Fact]
    public async Task QueryAsync_ForwardsToInnerHandle()
    {
        var dispatcher = new InMemoryWorkflowDispatcher();
        var handle = await StartedHandleAsync<string>(dispatcher);
        dispatcher.ConfigureQueryHandler(handle.WorkflowId, "status", () => "ok");

        Result<string> result = await handle.QueryAsync<string>("status");

        Assert.True(result.IsSuccess);
        Assert.Equal("ok", result.Value);
        handle.ShouldHaveBeenQueried("status");
        Assert.Contains("status", handle.QueriesReceived);
    }

    [Fact]
    public async Task CancelAsync_ForwardsToInnerHandle()
    {
        var dispatcher = new InMemoryWorkflowDispatcher();
        var handle = await StartedHandleAsync<string>(dispatcher);

        Result result = await handle.CancelAsync();

        Assert.True(result.IsSuccess);
        handle.ShouldHaveBeenCancelled();
        Assert.Equal(WorkflowLifecycleStatus.Cancelled, handle.Status);
    }

    [Fact]
    public async Task TerminateAsync_ForwardsToInnerHandle()
    {
        var dispatcher = new InMemoryWorkflowDispatcher();
        var handle = await StartedHandleAsync<string>(dispatcher);

        Result result = await handle.TerminateAsync("shutdown");

        Assert.True(result.IsSuccess);
        handle.ShouldHaveBeenTerminated("shutdown");
    }

    [Fact]
    public async Task SimulateFailure_ForwardsToInnerHandle_AffectsSignalAndCancelAndTerminate_NotQuery()
    {
        var dispatcher = new InMemoryWorkflowDispatcher();
        var handle = await StartedHandleAsync<string>(dispatcher);
        dispatcher.ConfigureQueryHandler(handle.WorkflowId, "status", () => "ok");

        handle.SimulateFailure = true;

        Assert.True((await handle.SignalAsync("go", "x")).IsFailure);
        Assert.True((await handle.CancelAsync()).IsFailure);
        Assert.True((await handle.TerminateAsync("reason")).IsFailure);
        Assert.True((await handle.QueryAsync<string>("status")).IsSuccess);
    }

    [Fact]
    public async Task ShouldNotHaveSignalled_ForwardsToInnerHandle()
    {
        var dispatcher = new InMemoryWorkflowDispatcher();
        var handle = await StartedHandleAsync<string>(dispatcher);

        handle.ShouldNotHaveSignalled("never-sent");
    }

    [Fact]
    public async Task ShouldHaveSignalledOfT_ForwardsToInnerHandle_AndCastsArgs()
    {
        var dispatcher = new InMemoryWorkflowDispatcher();
        var handle = await StartedHandleAsync<string>(dispatcher);

        await handle.SignalAsync("count", 7);

        Assert.Equal(7, handle.ShouldHaveSignalled<int>("count"));
    }
}

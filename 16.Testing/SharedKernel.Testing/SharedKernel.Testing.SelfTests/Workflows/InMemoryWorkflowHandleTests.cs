using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;
using SharedKernel.Testing.Workflows;
using SharedKernel.Workflows.Temporal.Dispatch;
using SharedKernel.Workflows.Temporal.Errors;

namespace SharedKernel.Testing.SelfTests.Workflows;

/// <summary>
/// Proves <see cref="InMemoryWorkflowHandle"/> against <c>IWorkflowHandle</c>'s documented
/// signal/query/cancel/terminate contract -- no consuming domain has adopted this fake yet, so this
/// self-test is the only behavioral proof today, per the SelfTests routing rule (T-55/P-288/WO-046).
/// </summary>
public sealed class InMemoryWorkflowHandleTests
{
    private static async Task<InMemoryWorkflowHandle> StartedHandleAsync(InMemoryWorkflowDispatcher dispatcher, string businessKey = "k1")
    {
        Result<IWorkflowHandle> result = await dispatcher.StartAsync<SampleWorkflow>(
            WorkflowsTestFixtures.ValidOptions(businessKey), TenantScope.Of("tenant-a"));
        Assert.True(result.IsSuccess);
        return (InMemoryWorkflowHandle)result.Value;
    }

    // --- SignalAsync ---

    [Fact]
    public async Task SignalAsync_ExistingExecution_Succeeds_AndIsRecorded()
    {
        var dispatcher = new InMemoryWorkflowDispatcher();
        var handle = await StartedHandleAsync(dispatcher);

        Result result = await handle.SignalAsync("advance", "step-1");

        Assert.True(result.IsSuccess);
        var recordedArgs = handle.ShouldHaveSignalled("advance");
        Assert.Equal("step-1", recordedArgs);
    }

    [Fact]
    public async Task SignalAsync_TypedShouldHaveSignalled_CastsRecordedArgs()
    {
        var dispatcher = new InMemoryWorkflowDispatcher();
        var handle = await StartedHandleAsync(dispatcher);

        await handle.SignalAsync("advance", 42);

        Assert.Equal(42, handle.ShouldHaveSignalled<int>("advance"));
    }

    [Fact]
    public async Task SignalAsync_NoBackingExecution_ReturnsNotFound()
    {
        var dispatcher = new InMemoryWorkflowDispatcher();
        var handle = dispatcher.GetHandle("no-such-id", runId: null, TenantScope.Of("tenant-a"));

        Result result = await handle.SignalAsync("advance", "x");

        Assert.True(result.IsFailure);
        Assert.Equal(WorkflowErrors.NotFound("no-such-id"), result.Error);
    }

    [Fact]
    public async Task SignalAsync_SimulateFailure_ReturnsFailure_AndDoesNotRecord()
    {
        var dispatcher = new InMemoryWorkflowDispatcher();
        var handle = (InMemoryWorkflowHandle)await StartedHandleAsync(dispatcher);
        handle.SimulateFailure = true;

        Result result = await handle.SignalAsync("advance", "x");

        Assert.True(result.IsFailure);
        handle.ShouldNotHaveSignalled("advance");
    }

    [Fact]
    public async Task ShouldHaveSignalled_NoMatch_Throws()
    {
        var dispatcher = new InMemoryWorkflowDispatcher();
        var handle = await StartedHandleAsync(dispatcher);

        Assert.Throws<InvalidOperationException>(() => handle.ShouldHaveSignalled("never-sent"));
    }

    [Fact]
    public async Task ShouldNotHaveSignalled_Match_Throws()
    {
        var dispatcher = new InMemoryWorkflowDispatcher();
        var handle = await StartedHandleAsync(dispatcher);
        await handle.SignalAsync("advance", "x");

        Assert.Throws<InvalidOperationException>(() => handle.ShouldNotHaveSignalled("advance"));
    }

    // --- QueryAsync ---

    [Fact]
    public async Task QueryAsync_ConfiguredHandler_ReturnsConfiguredResult_AndIsRecorded()
    {
        var dispatcher = new InMemoryWorkflowDispatcher();
        var handle = await StartedHandleAsync(dispatcher);
        dispatcher.ConfigureQueryHandler(handle.WorkflowId, "status", () => "ok");

        Result<string> result = await handle.QueryAsync<string>("status");

        Assert.True(result.IsSuccess);
        Assert.Equal("ok", result.Value);
        handle.ShouldHaveBeenQueried("status");
    }

    [Fact]
    public async Task QueryAsync_UnconfiguredHandler_ReturnsQueryFailed_ButStillRecordsTheAttempt()
    {
        var dispatcher = new InMemoryWorkflowDispatcher();
        var handle = await StartedHandleAsync(dispatcher);

        Result<string> result = await handle.QueryAsync<string>("status");

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Unexpected, result.Error.Type);
        Assert.Equal(WorkflowErrors.QueryFailed("No query handler configured for 'status'."), result.Error);
        handle.ShouldHaveBeenQueried("status");
    }

    [Fact]
    public async Task QueryAsync_NoBackingExecution_ReturnsNotFound()
    {
        var dispatcher = new InMemoryWorkflowDispatcher();
        var handle = dispatcher.GetHandle("no-such-id", runId: null, TenantScope.Of("tenant-a"));

        Result<string> result = await handle.QueryAsync<string>("status");

        Assert.True(result.IsFailure);
        Assert.Equal(WorkflowErrors.NotFound("no-such-id"), result.Error);
    }

    [Fact]
    public async Task QueryAsync_IsUnaffectedBySimulateFailure()
    {
        var dispatcher = new InMemoryWorkflowDispatcher();
        var handle = (InMemoryWorkflowHandle)await StartedHandleAsync(dispatcher);
        dispatcher.ConfigureQueryHandler(handle.WorkflowId, "status", () => "ok");
        handle.SimulateFailure = true;

        Result<string> result = await handle.QueryAsync<string>("status");

        Assert.True(result.IsSuccess);
        Assert.Equal("ok", result.Value);
    }

    [Fact]
    public async Task ShouldHaveBeenQueried_NoMatch_Throws()
    {
        var dispatcher = new InMemoryWorkflowDispatcher();
        var handle = await StartedHandleAsync(dispatcher);

        Assert.Throws<InvalidOperationException>(() => handle.ShouldHaveBeenQueried("status"));
    }

    // --- CancelAsync ---

    [Fact]
    public async Task CancelAsync_RunningExecution_Succeeds_AndSetsStatusCancelled()
    {
        var dispatcher = new InMemoryWorkflowDispatcher();
        var handle = await StartedHandleAsync(dispatcher);

        Result result = await handle.CancelAsync();

        Assert.True(result.IsSuccess);
        handle.ShouldHaveBeenCancelled();
    }

    [Fact]
    public async Task CancelAsync_IsIdempotent_ReSucceedsWithoutChangingStatus()
    {
        var dispatcher = new InMemoryWorkflowDispatcher();
        var handle = await StartedHandleAsync(dispatcher);

        await handle.CancelAsync();
        Result second = await handle.CancelAsync();

        Assert.True(second.IsSuccess);
        handle.ShouldHaveBeenCancelled();
    }

    [Fact]
    public async Task CancelAsync_AfterTermination_DoesNotOverwriteTerminatedStatus_DistinguishableOutcomes()
    {
        var dispatcher = new InMemoryWorkflowDispatcher();
        var handle = await StartedHandleAsync(dispatcher);
        await handle.TerminateAsync("op action");

        Result cancelResult = await handle.CancelAsync();

        Assert.True(cancelResult.IsSuccess);
        handle.ShouldHaveBeenTerminated("op action");
        Assert.Throws<InvalidOperationException>(handle.ShouldHaveBeenCancelled);
    }

    [Fact]
    public async Task CancelAsync_NoBackingExecution_ReturnsNotFound()
    {
        var dispatcher = new InMemoryWorkflowDispatcher();
        var handle = dispatcher.GetHandle("no-such-id", runId: null, TenantScope.Of("tenant-a"));

        Result result = await handle.CancelAsync();

        Assert.True(result.IsFailure);
        Assert.Equal(WorkflowErrors.NotFound("no-such-id"), result.Error);
    }

    [Fact]
    public async Task CancelAsync_SimulateFailure_ReturnsFailure_AndDoesNotChangeStatus()
    {
        var dispatcher = new InMemoryWorkflowDispatcher();
        var handle = (InMemoryWorkflowHandle)await StartedHandleAsync(dispatcher);
        handle.SimulateFailure = true;

        Result result = await handle.CancelAsync();

        Assert.True(result.IsFailure);
        Assert.Equal(WorkflowLifecycleStatus.Running, handle.Status);
    }

    [Fact]
    public async Task ShouldHaveBeenCancelled_NotCancelled_Throws()
    {
        var dispatcher = new InMemoryWorkflowDispatcher();
        var handle = await StartedHandleAsync(dispatcher);

        Assert.Throws<InvalidOperationException>(handle.ShouldHaveBeenCancelled);
    }

    // --- TerminateAsync ---

    [Fact]
    public async Task TerminateAsync_RunningExecution_Succeeds_AndRecordsReason()
    {
        var dispatcher = new InMemoryWorkflowDispatcher();
        var handle = await StartedHandleAsync(dispatcher);

        Result result = await handle.TerminateAsync("operator requested shutdown");

        Assert.True(result.IsSuccess);
        handle.ShouldHaveBeenTerminated("operator requested shutdown");
    }

    [Fact]
    public async Task TerminateAsync_ShouldHaveBeenTerminated_WithoutExpectedReason_MatchesAnyReason()
    {
        var dispatcher = new InMemoryWorkflowDispatcher();
        var handle = await StartedHandleAsync(dispatcher);
        await handle.TerminateAsync("whatever reason");

        handle.ShouldHaveBeenTerminated();
    }

    [Fact]
    public async Task TerminateAsync_ShouldHaveBeenTerminated_WrongExpectedReason_Throws()
    {
        var dispatcher = new InMemoryWorkflowDispatcher();
        var handle = await StartedHandleAsync(dispatcher);
        await handle.TerminateAsync("actual reason");

        Assert.Throws<InvalidOperationException>(() => handle.ShouldHaveBeenTerminated("expected reason"));
    }

    [Fact]
    public async Task TerminateAsync_IsIdempotent_ReSucceedsWithoutChangingReason()
    {
        var dispatcher = new InMemoryWorkflowDispatcher();
        var handle = await StartedHandleAsync(dispatcher);

        await handle.TerminateAsync("first reason");
        Result second = await handle.TerminateAsync("second reason");

        Assert.True(second.IsSuccess);
        handle.ShouldHaveBeenTerminated("first reason");
    }

    [Fact]
    public async Task TerminateAsync_AfterCancellation_DoesNotOverwriteCancelledStatus_DistinguishableOutcomes()
    {
        var dispatcher = new InMemoryWorkflowDispatcher();
        var handle = await StartedHandleAsync(dispatcher);
        await handle.CancelAsync();

        Result terminateResult = await handle.TerminateAsync("too late");

        Assert.True(terminateResult.IsSuccess);
        handle.ShouldHaveBeenCancelled();
        Assert.Throws<InvalidOperationException>(() => handle.ShouldHaveBeenTerminated());
    }

    [Fact]
    public async Task TerminateAsync_NullOrWhitespaceReason_Throws()
    {
        var dispatcher = new InMemoryWorkflowDispatcher();
        var handle = await StartedHandleAsync(dispatcher);

        // ArgumentException.ThrowIfNullOrWhiteSpace throws the ArgumentNullException subclass for a
        // null argument specifically, and ArgumentException itself for empty/whitespace.
        await Assert.ThrowsAsync<ArgumentNullException>(() => handle.TerminateAsync(null!));
        await Assert.ThrowsAsync<ArgumentException>(() => handle.TerminateAsync("   "));
    }

    [Fact]
    public async Task TerminateAsync_NoBackingExecution_ReturnsNotFound()
    {
        var dispatcher = new InMemoryWorkflowDispatcher();
        var handle = dispatcher.GetHandle("no-such-id", runId: null, TenantScope.Of("tenant-a"));

        Result result = await handle.TerminateAsync("reason");

        Assert.True(result.IsFailure);
        Assert.Equal(WorkflowErrors.NotFound("no-such-id"), result.Error);
    }

    [Fact]
    public async Task TerminateAsync_SimulateFailure_ReturnsFailure_AndDoesNotChangeStatus()
    {
        var dispatcher = new InMemoryWorkflowDispatcher();
        var handle = (InMemoryWorkflowHandle)await StartedHandleAsync(dispatcher);
        handle.SimulateFailure = true;

        Result result = await handle.TerminateAsync("reason");

        Assert.True(result.IsFailure);
        Assert.Equal(WorkflowLifecycleStatus.Running, handle.Status);
    }

    // --- WorkflowId/RunId/Status surface ---

    [Fact]
    public void Status_NoBackingExecution_IsNull()
    {
        var dispatcher = new InMemoryWorkflowDispatcher();
        var handle = (InMemoryWorkflowHandle)dispatcher.GetHandle("no-such-id", runId: null, TenantScope.Of("tenant-a"));

        Assert.Null(handle.Status);
    }

    [Fact]
    public async Task Status_RunningExecution_IsRunning()
    {
        var dispatcher = new InMemoryWorkflowDispatcher();
        var handle = (InMemoryWorkflowHandle)await StartedHandleAsync(dispatcher);

        Assert.Equal(WorkflowLifecycleStatus.Running, handle.Status);
    }
}

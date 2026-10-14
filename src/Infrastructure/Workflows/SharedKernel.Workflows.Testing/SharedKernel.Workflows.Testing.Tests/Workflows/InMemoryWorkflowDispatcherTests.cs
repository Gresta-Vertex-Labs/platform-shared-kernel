using SharedKernel.Execution.Tenancy;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;
using SharedKernel.Testing.Workflows;
using SharedKernel.Workflows.Temporal.Dispatch;
using SharedKernel.Workflows.Temporal.Errors;

namespace SharedKernel.Testing.SelfTests.Workflows;

/// <summary>
/// Proves <see cref="InMemoryWorkflowDispatcher"/> against <c>IWorkflowDispatcher</c>'s documented
/// start/attach/describe contract -- no consuming domain has adopted this fake yet, so this self-test
/// is the only behavioral proof today, per the SelfTests routing rule (T-55/P-288/WO-046).
/// </summary>
public sealed class InMemoryWorkflowDispatcherTests
{
    // --- StartAsync (no args) ---

    [Fact]
    public async Task StartAsync_NoArgs_Succeeds_AndIsRecorded()
    {
        var dispatcher = new InMemoryWorkflowDispatcher();

        Result<IWorkflowHandle> result = await dispatcher.StartAsync<SampleWorkflow>(
            WorkflowsTestFixtures.ValidOptions(), TenantScope.For(WorkflowsTestFixtures.TenantA));

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value.WorkflowId);
        Assert.NotNull(result.Value.RunId);

        var record = Assert.Single(dispatcher.StartedWorkflows);
        Assert.Equal(nameof(SampleWorkflow), record.WorkflowTypeName);
        Assert.Equal(result.Value.WorkflowId, record.WorkflowId);
        Assert.Equal(TenantScope.For(WorkflowsTestFixtures.TenantA), record.TenantScope);
        Assert.Null(record.Args);
    }

    [Fact]
    public async Task StartAsync_NoArgs_TenantScopeNone_ReturnsTenantScopeMissing_NoStateMutation()
    {
        var dispatcher = new InMemoryWorkflowDispatcher();

        Result<IWorkflowHandle> result = await dispatcher.StartAsync<SampleWorkflow>(
            WorkflowsTestFixtures.ValidOptions(), TenantScope.Global);

        Assert.True(result.IsFailure);
        Assert.Equal(WorkflowErrors.TenantScopeMissing(), result.Error);
        Assert.Empty(dispatcher.StartedWorkflows);
    }

    // --- StartAsync (with args) ---

    [Fact]
    public async Task StartAsync_WithArgs_Succeeds_AndRecordsArgs()
    {
        var dispatcher = new InMemoryWorkflowDispatcher();

        Result<IWorkflowHandle> result = await dispatcher.StartAsync<SampleWorkflow, string>(
            "hello", WorkflowsTestFixtures.ValidOptions(), TenantScope.For(WorkflowsTestFixtures.TenantA));

        Assert.True(result.IsSuccess);
        var record = Assert.Single(dispatcher.StartedWorkflows);
        Assert.Equal("hello", record.Args);
    }

    [Fact]
    public async Task StartAsync_WithArgs_TenantScopeNone_ReturnsTenantScopeMissing_NoStateMutation()
    {
        var dispatcher = new InMemoryWorkflowDispatcher();

        Result<IWorkflowHandle> result = await dispatcher.StartAsync<SampleWorkflow, string>(
            "hello", WorkflowsTestFixtures.ValidOptions(), TenantScope.Global);

        Assert.True(result.IsFailure);
        Assert.Equal(WorkflowErrors.TenantScopeMissing(), result.Error);
        Assert.Empty(dispatcher.StartedWorkflows);
    }

    // --- StartAsync (with args and result) ---

    [Fact]
    public async Task StartAsync_WithArgsAndResult_Succeeds_AndReturnsTypedHandle()
    {
        var dispatcher = new InMemoryWorkflowDispatcher();

        Result<IWorkflowHandle<string>> result = await dispatcher.StartAsync<SampleWorkflow, string, string>(
            "hello", WorkflowsTestFixtures.ValidOptions(), TenantScope.For(WorkflowsTestFixtures.TenantA));

        Assert.True(result.IsSuccess);
        Assert.IsType<InMemoryWorkflowHandle<string>>(result.Value);
        Assert.Single(dispatcher.StartedWorkflows);
    }

    [Fact]
    public async Task StartAsync_WithArgsAndResult_TenantScopeNone_ReturnsTenantScopeMissing_NoStateMutation()
    {
        var dispatcher = new InMemoryWorkflowDispatcher();

        Result<IWorkflowHandle<string>> result = await dispatcher.StartAsync<SampleWorkflow, string, string>(
            "hello", WorkflowsTestFixtures.ValidOptions(), TenantScope.Global);

        Assert.True(result.IsFailure);
        Assert.Equal(WorkflowErrors.TenantScopeMissing(), result.Error);
        Assert.Empty(dispatcher.StartedWorkflows);
    }

    // --- AlreadyStarted ---

    [Fact]
    public async Task StartAsync_DuplicateBusinessKeyWhileRunning_ReturnsAlreadyStarted_AndDoesNotMutateState()
    {
        var dispatcher = new InMemoryWorkflowDispatcher();
        var options = WorkflowsTestFixtures.ValidOptions(businessKey: "order-1");

        Result<IWorkflowHandle> first = await dispatcher.StartAsync<SampleWorkflow>(options, TenantScope.For(WorkflowsTestFixtures.TenantA));
        Assert.True(first.IsSuccess);

        Result<IWorkflowHandle> second = await dispatcher.StartAsync<SampleWorkflow>(options, TenantScope.For(WorkflowsTestFixtures.TenantA));

        Assert.True(second.IsFailure);
        Assert.Equal(WorkflowErrors.AlreadyStarted(), second.Error);
        Assert.Single(dispatcher.StartedWorkflows);
    }

    [Fact]
    public async Task StartAsync_SameBusinessKey_DifferentTenant_BothSucceed()
    {
        var dispatcher = new InMemoryWorkflowDispatcher();
        var options = WorkflowsTestFixtures.ValidOptions(businessKey: "order-1");

        Result<IWorkflowHandle> tenantA = await dispatcher.StartAsync<SampleWorkflow>(options, TenantScope.For(WorkflowsTestFixtures.TenantA));
        Result<IWorkflowHandle> tenantB = await dispatcher.StartAsync<SampleWorkflow>(options, TenantScope.For(WorkflowsTestFixtures.TenantB));

        Assert.True(tenantA.IsSuccess);
        Assert.True(tenantB.IsSuccess);
        Assert.NotEqual(tenantA.Value.WorkflowId, tenantB.Value.WorkflowId);
        Assert.Equal(2, dispatcher.StartedWorkflows.Count);
    }

    [Fact]
    public async Task StartAsync_SameBusinessKey_AfterTermination_SucceedsAgain()
    {
        var dispatcher = new InMemoryWorkflowDispatcher();
        var options = WorkflowsTestFixtures.ValidOptions(businessKey: "order-1");

        Result<IWorkflowHandle> first = await dispatcher.StartAsync<SampleWorkflow>(options, TenantScope.For(WorkflowsTestFixtures.TenantA));
        await first.Value.TerminateAsync("test cleanup");

        Result<IWorkflowHandle> second = await dispatcher.StartAsync<SampleWorkflow>(options, TenantScope.For(WorkflowsTestFixtures.TenantA));

        Assert.True(second.IsSuccess);
        Assert.Equal(2, dispatcher.StartedWorkflows.Count);
    }

    // --- SimulateFailure ---

    [Fact]
    public async Task StartAsync_SimulateFailure_ReturnsServiceUnavailable_AndDoesNotMutateState()
    {
        var dispatcher = new InMemoryWorkflowDispatcher { SimulateFailure = true };

        Result<IWorkflowHandle> result = await dispatcher.StartAsync<SampleWorkflow>(
            WorkflowsTestFixtures.ValidOptions(), TenantScope.For(WorkflowsTestFixtures.TenantA));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Unavailable, result.Error.Type);
        Assert.Empty(dispatcher.StartedWorkflows);
    }

    [Fact]
    public async Task DescribeAsync_SimulateFailure_ReturnsServiceUnavailable()
    {
        var dispatcher = new InMemoryWorkflowDispatcher { SimulateFailure = true };

        Result<WorkflowExecutionDescription> result = await dispatcher.DescribeAsync(
            WorkflowsTestFixtures.OwnId(), TenantScope.For(WorkflowsTestFixtures.TenantA));

        Assert.True(result.IsFailure);
        Assert.Equal(WorkflowErrorCodes.ServiceUnavailable, result.Error.Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public async Task DescribeAsync_BlankWorkflowId_ReturnsInvalidWorkflowId(string workflowId)
    {
        var dispatcher = new InMemoryWorkflowDispatcher();

        Result<WorkflowExecutionDescription> result = await dispatcher.DescribeAsync(workflowId, TenantScope.For(WorkflowsTestFixtures.TenantA));

        Assert.Equal(WorkflowErrorCodes.InvalidWorkflowId, result.Error.Code);
    }

    // --- GetHandle (non-generic) ---

    [Fact]
    public void GetHandle_NeverRoundTrips_ReturnsHandleEvenWithNoBackingExecution()
    {
        var dispatcher = new InMemoryWorkflowDispatcher();

        Result<IWorkflowHandle> result = dispatcher.GetHandle(WorkflowsTestFixtures.OwnId(), runId: null, TenantScope.For(WorkflowsTestFixtures.TenantA));

        Assert.True(result.IsSuccess);
        Assert.Equal(WorkflowsTestFixtures.OwnId(), result.Value.WorkflowId);
        Assert.Null(result.Value.RunId);
    }

    [Fact]
    public async Task GetHandle_ForAlreadyStartedWorkflow_AttachesToSharedState()
    {
        var dispatcher = new InMemoryWorkflowDispatcher();
        Result<IWorkflowHandle> started = await dispatcher.StartAsync<SampleWorkflow>(
            WorkflowsTestFixtures.ValidOptions(), TenantScope.For(WorkflowsTestFixtures.TenantA));

        IWorkflowHandle attached = dispatcher.GetHandle(started.Value.WorkflowId, runId: null, TenantScope.For(WorkflowsTestFixtures.TenantA)).Value;
        await attached.SignalAsync("go", "payload");

        // The originally-returned handle is a thin view over the SAME backing execution.
        var typedOriginal = (InMemoryWorkflowHandle)started.Value;
        Assert.Contains(typedOriginal.SignalsReceived, s => s.SignalName == "go");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void GetHandle_BlankWorkflowId_ReturnsInvalidWorkflowId(string? workflowId)
    {
        var dispatcher = new InMemoryWorkflowDispatcher();

        Result<IWorkflowHandle> result = dispatcher.GetHandle(workflowId!, runId: null, TenantScope.For(WorkflowsTestFixtures.TenantA));

        Assert.True(result.IsFailure);
        Assert.Equal(WorkflowErrorCodes.InvalidWorkflowId, result.Error.Code);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
    }

    [Fact]
    public void GetHandle_TenantScopeNone_ReturnsTenantScopeMissing()
    {
        var dispatcher = new InMemoryWorkflowDispatcher();

        Result<IWorkflowHandle> result = dispatcher.GetHandle(WorkflowsTestFixtures.OwnId(), runId: null, TenantScope.Global);

        Assert.True(result.IsFailure);
        Assert.Equal(WorkflowErrorCodes.TenantScopeMissing, result.Error.Code);
        Assert.Equal(ErrorType.Unauthorized, result.Error.Type);
    }

    [Fact]
    public async Task GetHandle_ForAForeignTenantsId_ReturnsNotFound_LikeAMissingExecution()
    {
        var dispatcher = new InMemoryWorkflowDispatcher();
        Result<IWorkflowHandle> started = await dispatcher.StartAsync<SampleWorkflow>(
            WorkflowsTestFixtures.ValidOptions(), TenantScope.For(WorkflowsTestFixtures.TenantA));

        Result<IWorkflowHandle> foreign = dispatcher.GetHandle(started.Value.WorkflowId, runId: null, TenantScope.For(WorkflowsTestFixtures.TenantB));
        Result<WorkflowExecutionDescription> describedForeign = await dispatcher.DescribeAsync(started.Value.WorkflowId, TenantScope.For(WorkflowsTestFixtures.TenantB));
        Result<WorkflowExecutionDescription> describedMissing = await dispatcher.DescribeAsync(WorkflowsTestFixtures.ForeignId("missing"), TenantScope.For(WorkflowsTestFixtures.TenantB));

        Assert.True(foreign.IsFailure);
        Assert.Equal(WorkflowErrorCodes.NotFound, foreign.Error.Code);
        Assert.Equal(ErrorType.NotFound, foreign.Error.Type);
        Assert.Equal(describedMissing.Error, describedForeign.Error);
        Assert.Equal(foreign.Error, describedForeign.Error);
    }

    // --- GetHandle<TResult> ---

    [Fact]
    public void GetHandleOfT_NeverRoundTrips_ReturnsHandleEvenWithNoBackingExecution()
    {
        var dispatcher = new InMemoryWorkflowDispatcher();

        Result<IWorkflowHandle<string>> result = dispatcher.GetHandle<string>(WorkflowsTestFixtures.OwnId(), runId: null, TenantScope.For(WorkflowsTestFixtures.TenantA));

        Assert.True(result.IsSuccess);
        Assert.Equal(WorkflowsTestFixtures.OwnId(), result.Value.WorkflowId);
    }

    [Fact]
    public void GetHandleOfT_TenantScopeNone_ReturnsTenantScopeMissing()
    {
        var dispatcher = new InMemoryWorkflowDispatcher();

        Result<IWorkflowHandle<string>> result = dispatcher.GetHandle<string>(WorkflowsTestFixtures.OwnId(), runId: null, TenantScope.Global);

        Assert.Equal(WorkflowErrorCodes.TenantScopeMissing, result.Error.Code);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public void GetHandleOfT_BlankWorkflowId_ReturnsInvalidWorkflowId(string? workflowId)
    {
        var dispatcher = new InMemoryWorkflowDispatcher();

        Result<IWorkflowHandle<string>> result = dispatcher.GetHandle<string>(workflowId!, runId: null, TenantScope.For(WorkflowsTestFixtures.TenantA));

        Assert.Equal(WorkflowErrorCodes.InvalidWorkflowId, result.Error.Code);
    }

    [Fact]
    public void GetHandleOfT_ForAForeignTenantsId_ReturnsNotFound()
    {
        var dispatcher = new InMemoryWorkflowDispatcher();

        Result<IWorkflowHandle<string>> result = dispatcher.GetHandle<string>(WorkflowsTestFixtures.ForeignId(), runId: null, TenantScope.For(WorkflowsTestFixtures.TenantA));

        Assert.Equal(WorkflowErrorCodes.NotFound, result.Error.Code);
    }

    // --- DescribeAsync ---

    [Fact]
    public async Task DescribeAsync_ExistingExecution_ReturnsDescription()
    {
        var dispatcher = new InMemoryWorkflowDispatcher();
        Result<IWorkflowHandle> started = await dispatcher.StartAsync<SampleWorkflow>(
            WorkflowsTestFixtures.ValidOptions(taskQueue: "queue-x"), TenantScope.For(WorkflowsTestFixtures.TenantA));

        Result<WorkflowExecutionDescription> result = await dispatcher.DescribeAsync(started.Value.WorkflowId, TenantScope.For(WorkflowsTestFixtures.TenantA));

        Assert.True(result.IsSuccess);
        Assert.Equal(started.Value.WorkflowId, result.Value.Id);
        Assert.Equal(started.Value.RunId, result.Value.RunId);
        Assert.Equal(nameof(SampleWorkflow), result.Value.WorkflowType);
        Assert.Equal("queue-x", result.Value.TaskQueue);
        Assert.Null(result.Value.CloseTime);
    }

    [Fact]
    public async Task DescribeAsync_TerminatedExecution_HasCloseTimeSet()
    {
        var dispatcher = new InMemoryWorkflowDispatcher();
        Result<IWorkflowHandle> started = await dispatcher.StartAsync<SampleWorkflow>(
            WorkflowsTestFixtures.ValidOptions(), TenantScope.For(WorkflowsTestFixtures.TenantA));
        await started.Value.TerminateAsync("done");

        Result<WorkflowExecutionDescription> result = await dispatcher.DescribeAsync(started.Value.WorkflowId, TenantScope.For(WorkflowsTestFixtures.TenantA));

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value.CloseTime);
    }

    [Fact]
    public async Task DescribeAsync_NoBackingExecution_ReturnsNotFound()
    {
        var dispatcher = new InMemoryWorkflowDispatcher();

        Result<WorkflowExecutionDescription> result = await dispatcher.DescribeAsync(WorkflowsTestFixtures.OwnId(), TenantScope.For(WorkflowsTestFixtures.TenantA));

        Assert.True(result.IsFailure);
        Assert.Equal(WorkflowErrors.NotFound(), result.Error);
    }

    [Fact]
    public async Task DescribeAsync_TenantScopeNone_ReturnsTenantScopeMissing()
    {
        var dispatcher = new InMemoryWorkflowDispatcher();

        Result<WorkflowExecutionDescription> result = await dispatcher.DescribeAsync(WorkflowsTestFixtures.OwnId(), TenantScope.Global);

        Assert.True(result.IsFailure);
        Assert.Equal(WorkflowErrors.TenantScopeMissing(), result.Error);
    }

    // --- ConfigureQueryHandler / CompleteWorkflow / FailWorkflow test-setup helpers ---

    [Fact]
    public void ConfigureQueryHandler_NoBackingExecution_Throws()
    {
        var dispatcher = new InMemoryWorkflowDispatcher();

        Assert.Throws<InvalidOperationException>(() =>
            dispatcher.ConfigureQueryHandler("no-such-id", "status", () => "value"));
    }

    [Fact]
    public async Task ConfigureQueryHandler_ThenQueryAsync_ReturnsConfiguredResult()
    {
        var dispatcher = new InMemoryWorkflowDispatcher();
        Result<IWorkflowHandle> started = await dispatcher.StartAsync<SampleWorkflow>(
            WorkflowsTestFixtures.ValidOptions(), TenantScope.For(WorkflowsTestFixtures.TenantA));
        dispatcher.ConfigureQueryHandler(started.Value.WorkflowId, "status", () => "running-nicely");

        Result<string> result = await started.Value.QueryAsync<string>("status");

        Assert.True(result.IsSuccess);
        Assert.Equal("running-nicely", result.Value);
    }

    [Fact]
    public void CompleteWorkflow_NoBackingExecution_Throws()
    {
        var dispatcher = new InMemoryWorkflowDispatcher();

        Assert.Throws<InvalidOperationException>(() => dispatcher.CompleteWorkflow("no-such-id", "result"));
    }

    [Fact]
    public void FailWorkflow_NoBackingExecution_Throws()
    {
        var dispatcher = new InMemoryWorkflowDispatcher();

        Assert.Throws<InvalidOperationException>(() =>
            dispatcher.FailWorkflow("no-such-id", Error.Unexpected("x", "y")));
    }

    [Fact]
    public async Task CompleteWorkflow_SetsStatusCompleted()
    {
        var dispatcher = new InMemoryWorkflowDispatcher();
        Result<IWorkflowHandle<string>> started = await dispatcher.StartAsync<SampleWorkflow, string, string>(
            "in", WorkflowsTestFixtures.ValidOptions(), TenantScope.For(WorkflowsTestFixtures.TenantA));

        dispatcher.CompleteWorkflow(started.Value.WorkflowId, "out");

        Assert.Equal(WorkflowLifecycleStatus.Completed, ((InMemoryWorkflowHandle<string>)started.Value).Status);
    }

    [Fact]
    public async Task FailWorkflow_SetsStatusFailed()
    {
        var dispatcher = new InMemoryWorkflowDispatcher();
        Result<IWorkflowHandle<string>> started = await dispatcher.StartAsync<SampleWorkflow, string, string>(
            "in", WorkflowsTestFixtures.ValidOptions(), TenantScope.For(WorkflowsTestFixtures.TenantA));

        dispatcher.FailWorkflow(started.Value.WorkflowId, Error.Unexpected("boom", "it broke"));

        Assert.Equal(WorkflowLifecycleStatus.Failed, ((InMemoryWorkflowHandle<string>)started.Value).Status);
    }

    // --- ShouldHaveStarted* / ShouldNotHaveStarted assertion helpers ---

    [Fact]
    public async Task ShouldHaveStarted_MatchingType_ReturnsRecord()
    {
        var dispatcher = new InMemoryWorkflowDispatcher();
        await dispatcher.StartAsync<SampleWorkflow>(WorkflowsTestFixtures.ValidOptions(), TenantScope.For(WorkflowsTestFixtures.TenantA));

        var record = dispatcher.ShouldHaveStarted<SampleWorkflow>();

        Assert.Equal(nameof(SampleWorkflow), record.WorkflowTypeName);
    }

    [Fact]
    public void ShouldHaveStarted_NoMatch_Throws()
    {
        var dispatcher = new InMemoryWorkflowDispatcher();

        Assert.Throws<InvalidOperationException>(() => dispatcher.ShouldHaveStarted<SampleWorkflow>());
    }

    [Fact]
    public async Task ShouldHaveStartedOnce_ExactlyOne_ReturnsRecord()
    {
        var dispatcher = new InMemoryWorkflowDispatcher();
        await dispatcher.StartAsync<SampleWorkflow>(WorkflowsTestFixtures.ValidOptions(), TenantScope.For(WorkflowsTestFixtures.TenantA));

        var record = dispatcher.ShouldHaveStartedOnce<SampleWorkflow>();

        Assert.Equal(nameof(SampleWorkflow), record.WorkflowTypeName);
    }

    [Fact]
    public async Task ShouldHaveStartedOnce_MoreThanOne_Throws()
    {
        var dispatcher = new InMemoryWorkflowDispatcher();
        await dispatcher.StartAsync<SampleWorkflow>(WorkflowsTestFixtures.ValidOptions(businessKey: "k1"), TenantScope.For(WorkflowsTestFixtures.TenantA));
        await dispatcher.StartAsync<SampleWorkflow>(WorkflowsTestFixtures.ValidOptions(businessKey: "k2"), TenantScope.For(WorkflowsTestFixtures.TenantA));

        Assert.Throws<InvalidOperationException>(() => dispatcher.ShouldHaveStartedOnce<SampleWorkflow>());
    }

    [Fact]
    public void ShouldHaveStartedOnce_Zero_Throws()
    {
        var dispatcher = new InMemoryWorkflowDispatcher();

        Assert.Throws<InvalidOperationException>(() => dispatcher.ShouldHaveStartedOnce<SampleWorkflow>());
    }

    [Fact]
    public void ShouldNotHaveStarted_NoMatch_DoesNotThrow()
    {
        var dispatcher = new InMemoryWorkflowDispatcher();

        dispatcher.ShouldNotHaveStarted<SampleWorkflow>();
    }

    [Fact]
    public async Task ShouldNotHaveStarted_Match_Throws()
    {
        var dispatcher = new InMemoryWorkflowDispatcher();
        await dispatcher.StartAsync<SampleWorkflow>(WorkflowsTestFixtures.ValidOptions(), TenantScope.For(WorkflowsTestFixtures.TenantA));

        Assert.Throws<InvalidOperationException>(() => dispatcher.ShouldNotHaveStarted<SampleWorkflow>());
    }

    [Fact]
    public async Task ShouldHaveStarted_DistinguishesBetweenWorkflowTypes()
    {
        var dispatcher = new InMemoryWorkflowDispatcher();
        await dispatcher.StartAsync<SampleWorkflow>(WorkflowsTestFixtures.ValidOptions(), TenantScope.For(WorkflowsTestFixtures.TenantA));

        dispatcher.ShouldNotHaveStarted<OtherWorkflow>();
        Assert.Throws<InvalidOperationException>(() => dispatcher.ShouldHaveStarted<OtherWorkflow>());
    }

    // --- Reset ---

    [Fact]
    public async Task Reset_ClearsBackingExecutionsAndStartedWorkflows()
    {
        var dispatcher = new InMemoryWorkflowDispatcher();
        Result<IWorkflowHandle> started = await dispatcher.StartAsync<SampleWorkflow>(
            WorkflowsTestFixtures.ValidOptions(), TenantScope.For(WorkflowsTestFixtures.TenantA));

        dispatcher.Reset();

        Assert.Empty(dispatcher.StartedWorkflows);
        Result<WorkflowExecutionDescription> result = await dispatcher.DescribeAsync(started.Value.WorkflowId, TenantScope.For(WorkflowsTestFixtures.TenantA));
        Assert.True(result.IsFailure);
        Assert.Equal(WorkflowErrors.NotFound(), result.Error);
    }

    // --- Custom IWorkflowIdFactory honored ---

    [Fact]
    public async Task Constructor_CustomWorkflowIdFactory_IsHonoredOverDefault()
    {
        var factory = new FixedWorkflowIdFactory("custom-id-123");
        var dispatcher = new InMemoryWorkflowDispatcher(factory);

        Result<IWorkflowHandle> result = await dispatcher.StartAsync<SampleWorkflow>(
            WorkflowsTestFixtures.ValidOptions(), TenantScope.For(WorkflowsTestFixtures.TenantA));

        Assert.True(result.IsSuccess);
        Assert.Equal("custom-id-123", result.Value.WorkflowId);
    }

    private sealed class FixedWorkflowIdFactory(string fixedId) : IWorkflowIdFactory
    {
        public string Create(string workflowTypeName, string businessKey, TenantScope tenantScope) => fixedId;
    }
}

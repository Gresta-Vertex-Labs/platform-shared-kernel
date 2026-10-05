using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Primitives.Results;
using SharedKernel.Workflows.Temporal.Dispatch;
using SharedKernel.Workflows.Temporal.Errors;
using Temporalio.Api.Enums.V1;

namespace SharedKernel.Workflows.Temporal.Tests.RealEnvironment;

/// <summary>
/// T-12 — idempotency: starting the same workflow id twice returns
/// <see cref="WorkflowErrors.AlreadyStarted"/> rather than producing a second execution, and both
/// <c>IdReusePolicy</c>/<c>IdConflictPolicy</c> settings behave as documented. This is the platform's
/// durable idempotency primitive; it is asserted, not assumed.
/// </summary>
[Collection(TemporalEnvironmentCollection.Name)]
public sealed class IdempotencyTests(TemporalTestFixture fixture)
{
    private static WorkflowStartOptions Options(
        string businessKey,
        WorkflowIdReusePolicy reuse = WorkflowIdReusePolicy.AllowDuplicate,
        WorkflowIdConflictPolicy conflict = WorkflowIdConflictPolicy.Fail) => new()
    {
        TaskQueue = TemporalTestFixture.TaskQueue,
        BusinessKey = businessKey,
        IdReusePolicy = reuse,
        IdConflictPolicy = conflict,
    };

    // IWorkflowHandle.RunId is NOT populated by StartAsync's underlying Temporal call — verified
    // empirically at Tests phase (T-12): it is null immediately after starting and only becomes
    // available through a real server round trip (DescribeAsync). Every RunId comparison below
    // therefore goes through DescribeAsync rather than trusting the handle returned by StartAsync.
    private async Task<string> DescribedRunIdAsync(IWorkflowDispatcher dispatcher, string workflowId)
    {
        Result<WorkflowExecutionDescription> description = await dispatcher.DescribeAsync(workflowId, TenantScope.For(TestTenants.Idem));
        description.IsSuccess.Should().BeTrue();
        return description.Value.RunId;
    }

    [Fact]
    public async Task StartingSameRunningId_WithRejectDuplicateAndFailConflict_ReturnsAlreadyStarted_NoSecondExecution()
    {
        using IServiceScope scope = fixture.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IWorkflowDispatcher>();
        string businessKey = $"idem-running-{Guid.NewGuid():N}";

        Result<IWorkflowHandle> first = await dispatcher.StartAsync<DelayWorkflow>(
            Options(businessKey, WorkflowIdReusePolicy.RejectDuplicate, WorkflowIdConflictPolicy.Fail),
            TenantScope.For(TestTenants.Idem));
        first.IsSuccess.Should().BeTrue();
        string originalRunId = await DescribedRunIdAsync(dispatcher, first.Value.WorkflowId);

        Result<IWorkflowHandle> second = await dispatcher.StartAsync<DelayWorkflow>(
            Options(businessKey, WorkflowIdReusePolicy.RejectDuplicate, WorkflowIdConflictPolicy.Fail),
            TenantScope.For(TestTenants.Idem));

        second.IsFailure.Should().BeTrue();
        second.Error.Code.Should().Be(WorkflowErrors.AlreadyStarted(first.Value.WorkflowId).Code);

        // No second execution: describing the id still reports the ORIGINAL run.
        string runIdAfterRejection = await DescribedRunIdAsync(dispatcher, first.Value.WorkflowId);
        runIdAfterRejection.Should().Be(originalRunId);
    }

    [Fact]
    public async Task StartingSameRunningId_WithUseExistingConflictPolicy_AttachesToTheSameExecution()
    {
        using IServiceScope scope = fixture.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IWorkflowDispatcher>();
        string businessKey = $"idem-useexisting-{Guid.NewGuid():N}";

        Result<IWorkflowHandle> first = await dispatcher.StartAsync<DelayWorkflow>(
            Options(businessKey, WorkflowIdReusePolicy.RejectDuplicate, WorkflowIdConflictPolicy.UseExisting),
            TenantScope.For(TestTenants.Idem));
        first.IsSuccess.Should().BeTrue();
        string firstRunId = await DescribedRunIdAsync(dispatcher, first.Value.WorkflowId);

        Result<IWorkflowHandle> second = await dispatcher.StartAsync<DelayWorkflow>(
            Options(businessKey, WorkflowIdReusePolicy.RejectDuplicate, WorkflowIdConflictPolicy.UseExisting),
            TenantScope.For(TestTenants.Idem));

        second.IsSuccess.Should().BeTrue(because: "UseExisting must attach to the running execution rather than fail");
        second.Value.WorkflowId.Should().Be(first.Value.WorkflowId);
        string secondRunId = await DescribedRunIdAsync(dispatcher, second.Value.WorkflowId);
        secondRunId.Should().Be(firstRunId, because: "no second execution was produced");
    }

    [Fact]
    public async Task AfterClosing_AllowDuplicateReusePolicy_StartsAGenuinelyNewExecution()
    {
        using IServiceScope scope = fixture.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IWorkflowDispatcher>();
        string businessKey = $"idem-allowduplicate-{Guid.NewGuid():N}";

        Result<IWorkflowHandle> first = await dispatcher.StartAsync<DelayWorkflow>(
            Options(businessKey, WorkflowIdReusePolicy.AllowDuplicate, WorkflowIdConflictPolicy.Fail),
            TenantScope.For(TestTenants.Idem));
        first.IsSuccess.Should().BeTrue();
        string firstRunId = await DescribedRunIdAsync(dispatcher, first.Value.WorkflowId);

        await first.Value.CancelAsync();
        var firstHandle = dispatcher.GetHandle<string>(first.Value.WorkflowId, runId: firstRunId, TenantScope.For(TestTenants.Idem));
        await firstHandle.GetResultAsync();

        Result<IWorkflowHandle> second = await dispatcher.StartAsync<DelayWorkflow>(
            Options(businessKey, WorkflowIdReusePolicy.AllowDuplicate, WorkflowIdConflictPolicy.Fail),
            TenantScope.For(TestTenants.Idem));

        second.IsSuccess.Should().BeTrue(because: "AllowDuplicate permits restarting under the same id once the prior execution has closed");
        second.Value.WorkflowId.Should().Be(first.Value.WorkflowId);
        string secondRunId = await DescribedRunIdAsync(dispatcher, second.Value.WorkflowId);
        secondRunId.Should().NotBe(firstRunId, because: "a genuinely new execution must have a new run id");
    }

    [Fact]
    public async Task AfterClosing_RejectDuplicateReusePolicy_StillReturnsAlreadyStarted()
    {
        using IServiceScope scope = fixture.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IWorkflowDispatcher>();
        string businessKey = $"idem-rejectduplicate-{Guid.NewGuid():N}";

        Result<IWorkflowHandle> first = await dispatcher.StartAsync<DelayWorkflow>(
            Options(businessKey, WorkflowIdReusePolicy.AllowDuplicate, WorkflowIdConflictPolicy.Fail),
            TenantScope.For(TestTenants.Idem));
        first.IsSuccess.Should().BeTrue();

        await first.Value.CancelAsync();
        var firstHandle = dispatcher.GetHandle<string>(first.Value.WorkflowId, runId: null, TenantScope.For(TestTenants.Idem));
        await firstHandle.GetResultAsync();

        Result<IWorkflowHandle> second = await dispatcher.StartAsync<DelayWorkflow>(
            Options(businessKey, WorkflowIdReusePolicy.RejectDuplicate, WorkflowIdConflictPolicy.Fail),
            TenantScope.For(TestTenants.Idem));

        second.IsFailure.Should().BeTrue(because: "RejectDuplicate never reuses the id regardless of the previous execution's closed state");
        second.Error.Code.Should().Be(WorkflowErrors.AlreadyStarted(first.Value.WorkflowId).Code);
    }
}

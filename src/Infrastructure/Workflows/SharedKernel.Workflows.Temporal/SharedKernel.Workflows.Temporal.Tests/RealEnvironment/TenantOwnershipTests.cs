using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;
using SharedKernel.Workflows.Temporal.Dispatch;
using SharedKernel.Workflows.Temporal.Errors;
using Temporalio.Api.Enums.V1;

namespace SharedKernel.Workflows.Temporal.Tests.RealEnvironment;

/// <summary>
/// A tenant cannot attach to, describe or signal another tenant's execution by guessing its composed id: the id's
/// tenant segment must be the caller's, and a foreign id is refused as <c>workflow.not_found</c>, the answer a missing
/// execution gets, so the refusal is no existence oracle.
/// </summary>
[Collection(TemporalEnvironmentCollection.Name)]
public sealed class TenantOwnershipTests(TemporalTestFixture fixture)
{
    private static WorkflowStartOptions Options(string businessKey) => new()
    {
        TaskQueue = TemporalTestFixture.TaskQueue,
        BusinessKey = businessKey,
        IdReusePolicy = WorkflowIdReusePolicy.AllowDuplicate,
        IdConflictPolicy = WorkflowIdConflictPolicy.Fail,
    };

    private async Task<(IWorkflowDispatcher Dispatcher, IWorkflowHandle Handle)> StartAsTenantAAsync(IServiceScope scope)
    {
        var dispatcher = scope.ServiceProvider.GetRequiredService<IWorkflowDispatcher>();
        Result<IWorkflowHandle> started = await dispatcher.StartAsync<CompensatingWorkflow, string>(
            $"ownership-{Guid.NewGuid():N}",
            Options($"ownership-{Guid.NewGuid():N}"),
            TenantScope.For(TestTenants.A));
        started.IsSuccess.Should().BeTrue();
        return (dispatcher, started.Value);
    }

    [Fact]
    public async Task TenantB_CannotAttachToTenantAsExecution_AndSeesTheSameAnswerAsForAMissingOne()
    {
        using IServiceScope scope = fixture.CreateScope();
        (IWorkflowDispatcher dispatcher, IWorkflowHandle handleOfA) = await StartAsTenantAAsync(scope);
        string missingForB = $"{TestTenants.B}:CompensatingWorkflow:never-started-{Guid.NewGuid():N}";

        Result<IWorkflowHandle> attach = dispatcher.GetHandle(handleOfA.WorkflowId, runId: null, TenantScope.For(TestTenants.B));
        Result<IWorkflowHandle<string>> attachTyped = dispatcher.GetHandle<string>(handleOfA.WorkflowId, runId: null, TenantScope.For(TestTenants.B));
        Result<WorkflowExecutionDescription> describeForeign = await dispatcher.DescribeAsync(handleOfA.WorkflowId, TenantScope.For(TestTenants.B));
        Result<WorkflowExecutionDescription> describeMissing = await dispatcher.DescribeAsync(missingForB, TenantScope.For(TestTenants.B));

        attach.IsFailure.Should().BeTrue();
        attach.Error.Code.Should().Be(WorkflowErrorCodes.NotFound);
        attach.Error.Type.Should().Be(ErrorType.NotFound);
        attachTyped.Error.Should().Be(attach.Error);
        describeForeign.Error.Should().Be(describeMissing.Error, because: "a foreign id and a missing id must be indistinguishable");
        describeForeign.Error.Message.Should().NotContain(handleOfA.WorkflowId);
    }

    [Fact]
    public async Task TenantBsOwnComposedId_ForTheSameBusinessKey_ReachesNoExecutionOfTenantA()
    {
        using IServiceScope scope = fixture.CreateScope();
        (IWorkflowDispatcher dispatcher, IWorkflowHandle handleOfA) = await StartAsTenantAAsync(scope);
        string businessKey = handleOfA.WorkflowId[(handleOfA.WorkflowId.LastIndexOf(':') + 1)..];
        string idOfB = new WorkflowIdFactory().Create("CompensatingWorkflow", businessKey, TenantScope.For(TestTenants.B));

        Result<IWorkflowHandle> attach = dispatcher.GetHandle(idOfB, runId: null, TenantScope.For(TestTenants.B));
        Result<string> queried = await attach.Value.QueryAsync<string>("GetMessage");

        attach.IsSuccess.Should().BeTrue(because: "the id is B's own; the guard checks ownership, not existence");
        queried.IsFailure.Should().BeTrue();
        queried.Error.Code.Should().Be(WorkflowErrorCodes.NotFound, because: "the execution belongs to tenant A under a different id");
    }

    [Fact]
    public async Task TenantA_StillAttachesDescribesAndSignalsItsOwnExecution_WhileBIsRefused()
    {
        using IServiceScope scope = fixture.CreateScope();
        (IWorkflowDispatcher dispatcher, IWorkflowHandle handleOfA) = await StartAsTenantAAsync(scope);

        dispatcher.GetHandle(handleOfA.WorkflowId, runId: null, TenantScope.For(TestTenants.B)).IsFailure.Should().BeTrue();
        Result<IWorkflowHandle> own = dispatcher.GetHandle(handleOfA.WorkflowId, runId: null, TenantScope.For(TestTenants.A));
        Result<WorkflowExecutionDescription> described = await dispatcher.DescribeAsync(handleOfA.WorkflowId, TenantScope.For(TestTenants.A));
        Result signalled = await own.Value.SignalAsync("SetMessage", "from-a");
        Result<string> message = await own.Value.QueryAsync<string>("GetMessage");

        own.IsSuccess.Should().BeTrue();
        described.IsSuccess.Should().BeTrue();
        signalled.IsSuccess.Should().BeTrue();
        message.Value.Should().Be("from-a", because: "tenant B's refused attempts changed nothing");
    }
}

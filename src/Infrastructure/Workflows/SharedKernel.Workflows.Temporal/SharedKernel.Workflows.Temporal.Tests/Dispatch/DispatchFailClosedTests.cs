using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SharedKernel.Primitives.Results;
using SharedKernel.Workflows.Temporal.Authoring;
using SharedKernel.Workflows.Temporal.Dispatch;
using SharedKernel.Workflows.Temporal.Errors;
using Temporalio.Api.Enums.V1;

namespace SharedKernel.Workflows.Temporal.Tests.Dispatch;

/// <summary>
/// T-05 (part 2) — a dispatch call made with <see cref="TenantScope.Global"/> returns
/// <see cref="WorkflowErrors.TenantScopeMissing"/> and performs <b>no I/O</b>, proven structurally via
/// a null <c>ITemporalClient</c>/<c>IWorkflowIdFactory</c> reference. Each rejection case is paired
/// with a companion proving the opposite once the guard passes — that the (null-backed) client is
/// genuinely reached — so the guard itself, not an accident of the fake, is what stopped the I/O.
/// </summary>
public sealed class DispatchFailClosedTests
{
    // No [Workflow] attribute needed — TenantScope.Global never lets the id-composition/Temporal call
    // path run, and the "guard passes" companion tests only need to prove the NULL CLIENT was reached,
    // not that a real Temporal call would succeed.
    private sealed class SampleWorkflow : WorkflowBase
    {
    }

    private static WorkflowDispatcher CreateDispatcherWithNoClient()
        => new(client: null!, idFactory: null!, logger: NullLogger<WorkflowDispatcher>.Instance);

    private static WorkflowStartOptions ValidOptions() => new()
    {
        TaskQueue = "test-queue",
        BusinessKey = "business-key-1",
        IdReusePolicy = WorkflowIdReusePolicy.AllowDuplicate,
        IdConflictPolicy = WorkflowIdConflictPolicy.Fail,
    };

    [Fact]
    public async Task StartAsync_NoArgs_TenantScopeNone_ReturnsTenantScopeMissing_NoIoPerformed()
    {
        WorkflowDispatcher dispatcher = CreateDispatcherWithNoClient();

        Result<IWorkflowHandle> result = await dispatcher.StartAsync<SampleWorkflow>(ValidOptions(), TenantScope.Global);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(WorkflowErrors.TenantScopeMissing().Code);
    }

    [Fact]
    public async Task StartAsync_NoArgs_ValidTenantScope_ReachesTheClient_ProvingTheGuardStoppedTheNoneCase()
    {
        WorkflowDispatcher dispatcher = CreateDispatcherWithNoClient();

        // The client/id-factory are both null — a valid tenant scope must reach past the guard and
        // attempt real work (composing the id, then calling the client), which throws a
        // NullReferenceException on the first null dependency it touches. This is the companion proof
        // that the None-case short-circuit above is what prevented I/O, not that I/O never happens on
        // this code path at all.
        Func<Task> act = () => dispatcher.StartAsync<SampleWorkflow>(ValidOptions(), TenantScope.For(TestTenants.A));

        await act.Should().ThrowAsync<NullReferenceException>();
    }

    [Fact]
    public async Task StartAsync_WithArgs_TenantScopeNone_ReturnsTenantScopeMissing_NoIoPerformed()
    {
        WorkflowDispatcher dispatcher = CreateDispatcherWithNoClient();

        Result<IWorkflowHandle> result = await dispatcher.StartAsync<SampleWorkflow, string>(
            "hello", ValidOptions(), TenantScope.Global);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(WorkflowErrors.TenantScopeMissing().Code);
    }

    [Fact]
    public async Task StartAsync_WithArgsAndResult_TenantScopeNone_ReturnsTenantScopeMissing_NoIoPerformed()
    {
        WorkflowDispatcher dispatcher = CreateDispatcherWithNoClient();

        Result<IWorkflowHandle<string>> result = await dispatcher.StartAsync<SampleWorkflow, string, string>(
            "hello", ValidOptions(), TenantScope.Global);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(WorkflowErrors.TenantScopeMissing().Code);
    }

    [Fact]
    public async Task DescribeAsync_TenantScopeNone_ReturnsTenantScopeMissing_NoIoPerformed()
    {
        WorkflowDispatcher dispatcher = CreateDispatcherWithNoClient();

        Result<WorkflowExecutionDescription> result = await dispatcher.DescribeAsync("some-id", TenantScope.Global);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(WorkflowErrors.TenantScopeMissing().Code);
    }

    [Fact]
    public async Task DescribeAsync_ValidTenantScope_ReachesTheClient_ProvingTheGuardStoppedTheNoneCase()
    {
        WorkflowDispatcher dispatcher = CreateDispatcherWithNoClient();

        // DescribeAsync wraps its client call in a catch-all that maps any exception (including the
        // NullReferenceException the null client produces here) through WorkflowFailureMapper.ToError
        // rather than letting it propagate — so the companion proof here is a MAPPED FAILURE, not a
        // thrown exception, distinguishing it from StartAsync/GetHandle (whose id-composition step
        // runs before any try/catch and therefore does propagate the raw exception).
        Result<WorkflowExecutionDescription> result = await dispatcher.DescribeAsync("some-id", TenantScope.For(TestTenants.A));

        result.IsFailure.Should().BeTrue(because: "the guard passed and the code genuinely attempted to reach the (absent) client");
        result.Error.Code.Should().NotBe(WorkflowErrors.TenantScopeMissing().Code);
    }

    [Fact]
    public void GetHandle_TenantScopeNone_ThrowsBeforeTouchingTheClient()
    {
        WorkflowDispatcher dispatcher = CreateDispatcherWithNoClient();

        Action act = () => dispatcher.GetHandle("some-id", runId: null, TenantScope.Global);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void GetHandle_ValidTenantScope_ReachesTheClient_ProvingTheGuardStoppedTheNoneCase()
    {
        WorkflowDispatcher dispatcher = CreateDispatcherWithNoClient();

        Action act = () => dispatcher.GetHandle("some-id", runId: null, TenantScope.For(TestTenants.A));

        act.Should().Throw<NullReferenceException>();
    }

    [Fact]
    public void GetHandleOfT_TenantScopeNone_ThrowsBeforeTouchingTheClient()
    {
        WorkflowDispatcher dispatcher = CreateDispatcherWithNoClient();

        Action act = () => dispatcher.GetHandle<string>("some-id", runId: null, TenantScope.Global);

        act.Should().Throw<ArgumentException>();
    }
}

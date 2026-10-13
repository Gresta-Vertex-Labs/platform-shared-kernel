using FluentAssertions;
using Microsoft.Extensions.Logging;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;
using SharedKernel.Testing.Logging;
using SharedKernel.Workflows.Temporal.Authoring;
using SharedKernel.Workflows.Temporal.Dispatch;
using SharedKernel.Workflows.Temporal.Errors;
using Temporalio.Api.Enums.V1;

namespace SharedKernel.Workflows.Temporal.Tests.Dispatch;

/// <summary>
/// Every dispatch member that can be refused locally is refused through one guard, with a <see cref="Result"/> and
/// <b>no I/O</b>: the client and id factory are <see langword="null"/>, so any call that reached them would throw. Each
/// refusal is paired with a companion proving the guard, not an accident of the fake, is what stopped the I/O.
/// </summary>
public sealed class DispatchFailClosedTests
{
    // No [Workflow] attribute needed: the guard never lets the Temporal call path run, and the "guard passes"
    // companions only prove the null client was reached.
    private sealed class SampleWorkflow : WorkflowBase
    {
    }

    private readonly InMemoryLogger<WorkflowDispatcher> _logger = new();

    private WorkflowDispatcher CreateDispatcherWithNoClient()
        => new(client: null!, idFactory: null!, logger: _logger);

    private static WorkflowStartOptions ValidOptions() => new()
    {
        TaskQueue = "test-queue",
        BusinessKey = "business-key-1",
        IdReusePolicy = WorkflowIdReusePolicy.AllowDuplicate,
        IdConflictPolicy = WorkflowIdConflictPolicy.Fail,
    };

    private static string OwnId(string key = "k") => $"{TestTenants.A}:SampleWorkflow:{key}";

    private static string ForeignId(string key = "k") => $"{TestTenants.B}:SampleWorkflow:{key}";

    [Fact]
    public async Task StartAsync_NoArgs_TenantScopeNone_ReturnsTenantScopeMissing_NoIoPerformed()
    {
        Result<IWorkflowHandle> result = await CreateDispatcherWithNoClient()
            .StartAsync<SampleWorkflow>(ValidOptions(), TenantScope.Global);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(WorkflowErrorCodes.TenantScopeMissing);
        result.Error.Type.Should().Be(ErrorType.Unauthorized);
    }

    [Fact]
    public async Task StartAsync_WithArgs_TenantScopeNone_ReturnsTenantScopeMissing_NoIoPerformed()
    {
        Result<IWorkflowHandle> result = await CreateDispatcherWithNoClient()
            .StartAsync<SampleWorkflow, string>("hello", ValidOptions(), TenantScope.Global);

        result.Error.Code.Should().Be(WorkflowErrorCodes.TenantScopeMissing);
    }

    [Fact]
    public async Task StartAsync_WithArgsAndResult_TenantScopeNone_ReturnsTenantScopeMissing_NoIoPerformed()
    {
        Result<IWorkflowHandle<string>> result = await CreateDispatcherWithNoClient()
            .StartAsync<SampleWorkflow, string, string>("hello", ValidOptions(), TenantScope.Global);

        result.Error.Code.Should().Be(WorkflowErrorCodes.TenantScopeMissing);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task StartAsync_BlankBusinessKey_ReturnsInvalidWorkflowId_NoIoPerformed(string businessKey)
    {
        Result<IWorkflowHandle> result = await CreateDispatcherWithNoClient()
            .StartAsync<SampleWorkflow>(ValidOptions() with { BusinessKey = businessKey }, TenantScope.For(TestTenants.A));

        result.Error.Code.Should().Be(WorkflowErrorCodes.InvalidWorkflowId);
        result.Error.Type.Should().Be(ErrorType.Validation);
    }

    [Fact]
    public async Task StartAsync_ValidTenantScope_ReachesTheClient_ProvingTheGuardStoppedTheNoneCase()
    {
        // The id factory and client are null, so a valid scope must get past the guard and fail on the first null
        // dependency it touches. A non-Temporal exception propagates; it is not turned into a Result.
        Func<Task> act = () => CreateDispatcherWithNoClient().StartAsync<SampleWorkflow>(ValidOptions(), TenantScope.For(TestTenants.A));

        await act.Should().ThrowAsync<NullReferenceException>();
    }

    [Fact]
    public async Task DescribeAsync_TenantScopeNone_ReturnsTenantScopeMissing_NoIoPerformed()
    {
        Result<WorkflowExecutionDescription> result = await CreateDispatcherWithNoClient()
            .DescribeAsync(OwnId(), TenantScope.Global);

        result.Error.Code.Should().Be(WorkflowErrorCodes.TenantScopeMissing);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public async Task DescribeAsync_BlankId_ReturnsInvalidWorkflowId_NoIoPerformed(string workflowId)
    {
        Result<WorkflowExecutionDescription> result = await CreateDispatcherWithNoClient()
            .DescribeAsync(workflowId, TenantScope.For(TestTenants.A));

        result.Error.Code.Should().Be(WorkflowErrorCodes.InvalidWorkflowId);
    }

    [Fact]
    public async Task DescribeAsync_AForeignTenantsId_IsWorkflowNotFound_NoIoPerformed()
    {
        Result<WorkflowExecutionDescription> result = await CreateDispatcherWithNoClient()
            .DescribeAsync(ForeignId(), TenantScope.For(TestTenants.A));

        result.Error.Should().Be(WorkflowErrors.NotFound());
        result.Error.Message.Should().NotContain(ForeignId());
    }

    [Fact]
    public async Task DescribeAsync_AnOwnId_ReachesTheClient_ProvingTheGuardStoppedTheOtherCases()
    {
        Func<Task> act = () => CreateDispatcherWithNoClient().DescribeAsync(OwnId(), TenantScope.For(TestTenants.A));

        await act.Should().ThrowAsync<NullReferenceException>();
    }

    [Fact]
    public void GetHandle_TenantScopeNone_ReturnsTenantScopeMissing_NoIoPerformed()
    {
        Result<IWorkflowHandle> result = CreateDispatcherWithNoClient().GetHandle(OwnId(), runId: null, TenantScope.Global);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(WorkflowErrorCodes.TenantScopeMissing);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void GetHandle_BlankId_ReturnsInvalidWorkflowId_NoIoPerformed(string workflowId)
    {
        Result<IWorkflowHandle> result = CreateDispatcherWithNoClient().GetHandle(workflowId, runId: null, TenantScope.For(TestTenants.A));

        result.Error.Code.Should().Be(WorkflowErrorCodes.InvalidWorkflowId);
    }

    [Fact]
    public void GetHandle_AForeignTenantsId_IsWorkflowNotFound_NoIoPerformed()
    {
        Result<IWorkflowHandle> result = CreateDispatcherWithNoClient().GetHandle(ForeignId(), runId: null, TenantScope.For(TestTenants.A));

        result.Error.Should().Be(WorkflowErrors.NotFound(), because: "a foreign id must be indistinguishable from a missing one");
    }

    [Fact]
    public void GetHandle_AnIdWithNoTenantSegment_IsWorkflowNotFound()
    {
        CreateDispatcherWithNoClient().GetHandle("some-id", runId: null, TenantScope.For(TestTenants.A)).Error.Code
            .Should().Be(WorkflowErrorCodes.NotFound);
    }

    [Fact]
    public void GetHandle_AnOwnId_ReachesTheClient_ProvingTheGuardStoppedTheOtherCases()
    {
        Action act = () => CreateDispatcherWithNoClient().GetHandle(OwnId(), runId: null, TenantScope.For(TestTenants.A));

        act.Should().Throw<NullReferenceException>();
    }

    [Fact]
    public void GetHandleOfT_TenantScopeNone_ReturnsTenantScopeMissing_NoIoPerformed()
    {
        Result<IWorkflowHandle<string>> result = CreateDispatcherWithNoClient().GetHandle<string>(OwnId(), runId: null, TenantScope.Global);

        result.Error.Code.Should().Be(WorkflowErrorCodes.TenantScopeMissing);
    }

    [Fact]
    public void GetHandleOfT_BlankId_ReturnsInvalidWorkflowId_NoIoPerformed()
    {
        CreateDispatcherWithNoClient().GetHandle<string>(" ", runId: null, TenantScope.For(TestTenants.A)).Error.Code
            .Should().Be(WorkflowErrorCodes.InvalidWorkflowId);
    }

    [Fact]
    public void GetHandleOfT_AForeignTenantsId_IsWorkflowNotFound_NoIoPerformed()
    {
        Result<IWorkflowHandle<string>> result = CreateDispatcherWithNoClient()
            .GetHandle<string>(ForeignId(), runId: null, TenantScope.For(TestTenants.A));

        result.Error.Should().Be(WorkflowErrors.NotFound());
    }

    [Fact]
    public void GetHandleOfT_AnOwnId_ReachesTheClient_ProvingTheGuardStoppedTheOtherCases()
    {
        Action act = () => CreateDispatcherWithNoClient().GetHandle<string>(OwnId(), runId: null, TenantScope.For(TestTenants.A));

        act.Should().Throw<NullReferenceException>();
    }

    [Fact]
    public void TheTenantSegmentComparison_DoesNotMatchAPrefixOfAnotherTenantsSegment()
    {
        // The tenant segment is a full GUID followed by the separator; an id that merely begins with the caller's tenant
        // text but continues it is not the caller's.
        string almostOwn = TestTenants.A + "0:SampleWorkflow:k";

        CreateDispatcherWithNoClient().GetHandle(almostOwn, runId: null, TenantScope.For(TestTenants.A)).Error.Code
            .Should().Be(WorkflowErrorCodes.NotFound);
    }

    [Fact]
    public async Task Refusals_AreLogged_WithErrorCodeAndTypeButNoWorkflowIdOrTenant()
    {
        WorkflowDispatcher dispatcher = CreateDispatcherWithNoClient();

        dispatcher.GetHandle(OwnId(), runId: null, TenantScope.Global);
        dispatcher.GetHandle(ForeignId("secret-key"), runId: null, TenantScope.For(TestTenants.A));
        dispatcher.GetHandle(" ", runId: null, TenantScope.For(TestTenants.A));
        await dispatcher.DescribeAsync(ForeignId("secret-key"), TenantScope.For(TestTenants.A));

        _logger.Records.ShouldHaveLogged(new EventId(17001), LogLevel.Warning).Template.Should().NotContain("WorkflowId");
        _logger.Records.ShouldHaveLoggedCount(new EventId(17014), 3);
        _logger.Records.ShouldHaveLoggedWithProperty(new EventId(17014), "ErrorCode", WorkflowErrorCodes.NotFound);
        _logger.Records.ShouldHaveLoggedWithProperty(new EventId(17014), "ErrorCode", WorkflowErrorCodes.InvalidWorkflowId);
        _logger.Records.ShouldNotLeak("secret-key", TestTenants.A.ToString(), TestTenants.B.ToString());
        _logger.Records.ShouldFollowLogConvention();
    }
}

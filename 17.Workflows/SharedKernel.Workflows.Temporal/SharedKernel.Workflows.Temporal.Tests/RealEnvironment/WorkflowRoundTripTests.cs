using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Primitives.Results;
using SharedKernel.Workflows.Temporal.Dispatch;
using Temporalio.Api.Enums.V1;

namespace SharedKernel.Workflows.Temporal.Tests.RealEnvironment;

/// <summary>
/// T-09 — real-environment workflow round trip via <c>WorkflowEnvironment.StartTimeSkippingAsync()</c>:
/// start a sample workflow through <see cref="IWorkflowDispatcher"/>, execute an activity through
/// <see cref="Authoring.WorkflowBase.ExecuteAsync{TActivity, TArgs, TResult}"/>, and await the result
/// through <see cref="IWorkflowHandle{TResult}"/>. Confirms the platform default
/// <c>StartToCloseTimeout</c> genuinely prevents Temporal's no-default-timeout rejection.
/// </summary>
[Collection(TemporalEnvironmentCollection.Name)]
public sealed class WorkflowRoundTripTests(TemporalTestFixture fixture)
{
    private static WorkflowStartOptions Options(string businessKey) => new()
    {
        TaskQueue = TemporalTestFixture.TaskQueue,
        BusinessKey = businessKey,
        IdReusePolicy = WorkflowIdReusePolicy.AllowDuplicate,
        IdConflictPolicy = WorkflowIdConflictPolicy.Fail,
    };

    [Fact]
    public async Task StartAsync_ExecuteAsync_GetResultAsync_RoundTripsThroughARealWorker()
    {
        using IServiceScope scope = fixture.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IWorkflowDispatcher>();

        Result<IWorkflowHandle<string>> startResult = await dispatcher.StartAsync<EchoWorkflow, string, string>(
            "round-trip-input",
            Options($"round-trip-{Guid.NewGuid():N}"),
            TenantScope.For(TestTenants.RoundTrip));

        startResult.IsSuccess.Should().BeTrue();

        Result<string> finalResult = await startResult.Value.GetResultAsync();

        finalResult.IsSuccess.Should().BeTrue();
        finalResult.Value.Should().Be("echo:round-trip-input");
    }

    [Fact]
    public async Task StartAsync_ReturnsBeforeCompletion_HandleThenAwaitsSeparately()
    {
        using IServiceScope scope = fixture.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IWorkflowDispatcher>();

        Result<IWorkflowHandle<string>> startResult = await dispatcher.StartAsync<EchoWorkflow, string, string>(
            "separate-await",
            Options($"separate-await-{Guid.NewGuid():N}"),
            TenantScope.For(TestTenants.RoundTrip));

        startResult.IsSuccess.Should().BeTrue();
        startResult.Value.WorkflowId.Should().NotBeNullOrWhiteSpace();

        // Attach to the SAME execution via GetHandle<T>, proving the handle is a durable reference,
        // not a one-shot completion future.
        var reattached = dispatcher.GetHandle<string>(startResult.Value.WorkflowId, runId: null, TenantScope.For(TestTenants.RoundTrip));

        Result<string> result = await reattached.GetResultAsync();
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("echo:separate-await");
    }
}

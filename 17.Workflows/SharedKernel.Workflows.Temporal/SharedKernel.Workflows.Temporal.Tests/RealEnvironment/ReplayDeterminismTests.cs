using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Primitives.Results;
using SharedKernel.Workflows.Temporal.Dispatch;
using Temporalio.Api.Enums.V1;
using Temporalio.Client;
using Temporalio.Common;
using Temporalio.Worker;

namespace SharedKernel.Workflows.Temporal.Tests.RealEnvironment;

/// <summary>
/// T-13 — history-replay determinism tests via <see cref="WorkflowReplayer"/>, the single
/// highest-value test in this domain. Capture a sample workflow's history and replay it against the
/// shipped code, then prove the test has teeth: replay the same history against a deliberately
/// non-deterministic variant and assert it <b>fails</b>.
/// </summary>
[Collection(TemporalEnvironmentCollection.Name)]
public sealed class ReplayDeterminismTests(TemporalTestFixture fixture)
{
    private static WorkflowStartOptions Options(string businessKey) => new()
    {
        TaskQueue = TemporalTestFixture.TaskQueue,
        BusinessKey = businessKey,
        IdReusePolicy = WorkflowIdReusePolicy.AllowDuplicate,
        IdConflictPolicy = WorkflowIdConflictPolicy.Fail,
    };

    private async Task<WorkflowHistory> CaptureEchoWorkflowHistoryAsync()
    {
        using IServiceScope scope = fixture.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IWorkflowDispatcher>();

        Result<IWorkflowHandle<string>> startResult = await dispatcher.StartAsync<EchoWorkflow, string, string>(
            "replay-target-input",
            Options($"replay-{Guid.NewGuid():N}"),
            TenantScope.For(TestTenants.Replay));
        startResult.IsSuccess.Should().BeTrue();
        await startResult.Value.GetResultAsync();

        WorkflowHandle rawHandle = fixture.Environment.Client.GetWorkflowHandle(startResult.Value.WorkflowId);
        return await rawHandle.FetchHistoryAsync();
    }

    [Fact]
    public async Task ReplayingRealHistory_AgainstTheShippedWorkflow_Succeeds()
    {
        WorkflowHistory history = await CaptureEchoWorkflowHistoryAsync();

        var replayerOptions = new WorkflowReplayerOptions
        {
            Namespace = fixture.Environment.Client.Options.Namespace,
            // Deliberately NOT TemporalTestFixture.TaskQueue: this standalone WorkflowReplayer is
            // never meant to attach to the shared fixture's own LIVE worker/task queue identity in
            // any way (it replays a captured WorkflowHistory purely in-memory) — using a distinct
            // name keeps that separation explicit rather than accidental.
            TaskQueue = "sk-workflows-tests-replayer-queue",
        };
        replayerOptions.AddWorkflow(typeof(EchoWorkflow));
        var replayer = new WorkflowReplayer(replayerOptions);

        WorkflowReplayResult result = await replayer.ReplayWorkflowAsync(history, throwOnReplayFailure: false);

        result.ReplayFailure.Should().BeNull(because: "the shipped EchoWorkflow code must replay the exact history it produced");
    }

    [Fact]
    public async Task ReplayingRealHistory_AgainstADeliberatelyNonDeterministicVariant_Fails()
    {
        WorkflowHistory history = await CaptureEchoWorkflowHistoryAsync();

        var replayerOptions = new WorkflowReplayerOptions
        {
            Namespace = fixture.Environment.Client.Options.Namespace,
            // Deliberately NOT TemporalTestFixture.TaskQueue: this standalone WorkflowReplayer is
            // never meant to attach to the shared fixture's own LIVE worker/task queue identity in
            // any way (it replays a captured WorkflowHistory purely in-memory) — using a distinct
            // name keeps that separation explicit rather than accidental.
            TaskQueue = "sk-workflows-tests-replayer-queue",
        };
        replayerOptions.AddWorkflow(typeof(BadEchoWorkflowVariant));
        var replayer = new WorkflowReplayer(replayerOptions);

        WorkflowReplayResult result = await replayer.ReplayWorkflowAsync(history, throwOnReplayFailure: false);

        result.ReplayFailure.Should().NotBeNull(
            because: "a variant that calls the activity twice instead of once diverges from the recorded history, " +
                     "proving this replay test has teeth rather than passing unconditionally");
    }
}

using System.Diagnostics;
using FluentAssertions;
using Temporalio.Api.Enums.V1;
using Temporalio.Client;

namespace SharedKernel.Workflows.Temporal.Tests.RealEnvironment;

/// <summary>
/// T-10 — time-skipping tests for the long-duration paths that are otherwise untestable: a 30-day
/// <c>Workflow.DelayAsync</c> completing in milliseconds, a timer-vs-signal race, an activity retry
/// exhausting its policy, and a <c>ScheduleToClose</c> timeout. This capability is the reason
/// <c>StartTimeSkippingAsync</c> is the default rather than <c>StartLocalAsync</c>.
/// </summary>
/// <remarks>
/// <b>Verified empirically at Tests phase (T-10):</b> genuine auto-time-skipping is coordinated
/// through the exact <see cref="ITemporalClient"/> instance
/// <c>WorkflowEnvironment.Client</c> vends — a client independently connected via
/// <c>Temporalio.Extensions.Hosting.AddTemporalClient(targetHost, ns)</c> to the very same server
/// (i.e. <see cref="IWorkflowDispatcher"/>'s own client, as built by
/// <c>AddSharedKernelTemporalWorkflows</c>) does <b>not</b> participate in that coordination — a
/// workflow depending on a real timer elapsing naturally never progresses through it (confirmed by a
/// standalone repro: the same composition pattern hangs indefinitely). This is a genuine constraint on
/// <em>how to test</em> time-skipping correctly, not a defect in the dispatcher; the fix is for
/// time-dependent tests to start/await through <c>fixture.Environment.Client</c> directly, while the
/// WORKER processing those workflows remains the one built by the production
/// <c>AddSharedKernelTemporalWorkflows</c> composition (task-queue routing does not care which client
/// started an execution). T-09's round trip and every other real-environment test already prove the
/// full <see cref="IWorkflowDispatcher"/> pipeline end to end where no genuine time-skip is required.
/// </remarks>
[Collection(TemporalEnvironmentCollection.Name)]
public sealed class TimeSkippingTests(TemporalTestFixture fixture)
{
    private static WorkflowOptions Options(string businessKey) => new(
        id: $"tenant-timeskip:{businessKey}",
        taskQueue: TemporalTestFixture.TaskQueue)
    {
        IdReusePolicy = WorkflowIdReusePolicy.AllowDuplicate,
        IdConflictPolicy = WorkflowIdConflictPolicy.Fail,
    };

    [Fact]
    public async Task ThirtyDayDelay_CompletesInMilliseconds_UnderTimeSkipping()
    {
        ITemporalClient client = fixture.Environment.Client;

        var stopwatch = Stopwatch.StartNew();
        WorkflowHandle handle = await client.StartWorkflowAsync(
            nameof(DelayWorkflow),
            [],
            Options($"delay-{Guid.NewGuid():N}"));

        string result = await handle.GetResultAsync<string>();
        stopwatch.Stop();

        result.Should().Be("delayed-done");
        stopwatch.Elapsed.Should().BeLessThan(
            TimeSpan.FromSeconds(30),
            because: "a genuine 30-day timer must be time-skipped, not really awaited");
    }

    [Fact]
    public async Task TimerVsSignal_SignalArrivesFirst_WorkflowReportsSignal()
    {
        ITemporalClient client = fixture.Environment.Client;

        WorkflowHandle handle = await client.StartWorkflowAsync(
            nameof(TimerVsSignalWorkflow),
            [],
            Options($"timer-vs-signal-win-{Guid.NewGuid():N}"));

        await handle.SignalAsync("Signal", ["go"]);

        string result = await handle.GetResultAsync<string>();

        result.Should().Be("signal");
    }

    [Fact]
    public async Task TimerVsSignal_NoSignalSent_TimerWinsUnderTimeSkipping()
    {
        ITemporalClient client = fixture.Environment.Client;

        WorkflowHandle handle = await client.StartWorkflowAsync(
            nameof(TimerVsSignalWorkflow),
            [],
            Options($"timer-vs-signal-lose-{Guid.NewGuid():N}"));

        string result = await handle.GetResultAsync<string>();

        result.Should().Be("timer");
    }

    [Fact]
    public async Task ActivityRetryPolicy_GenuinelyExhausts_WorkflowFails()
    {
        ITemporalClient client = fixture.Environment.Client;

        WorkflowHandle handle = await client.StartWorkflowAsync(
            nameof(RetryExhaustionWorkflow),
            ["will-always-fail"],
            Options($"retry-exhaustion-{Guid.NewGuid():N}"));

        Func<Task> act = () => handle.GetResultAsync<string>();

        await act.Should().ThrowAsync<Exception>(
            because: "the activity's retry policy (MaximumAttempts=2) must genuinely exhaust rather than succeed");
    }

    [Fact]
    public async Task ScheduleToCloseTimeout_ExpiresBeforeActivityEverCompletes()
    {
        ITemporalClient client = fixture.Environment.Client;

        WorkflowHandle handle = await client.StartWorkflowAsync(
            nameof(ScheduleToCloseTimeoutWorkflow),
            ["will-never-complete"],
            Options($"schedule-to-close-{Guid.NewGuid():N}"));

        Func<Task> act = () => handle.GetResultAsync<string>();

        await act.Should().ThrowAsync<Exception>(
            because: "the activity never completes, so its ScheduleToCloseTimeout must expire the workflow");
    }
}

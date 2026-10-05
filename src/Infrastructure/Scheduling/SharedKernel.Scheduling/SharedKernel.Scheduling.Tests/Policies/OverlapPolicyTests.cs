using FluentAssertions;
using SharedKernel.Scheduling.Policies;
using SharedKernel.Scheduling.Tests.TestSupport;
using Xunit;

namespace SharedKernel.Scheduling.Tests.Policies;

/// <summary>
/// T-03 — each <see cref="OverlapPolicy"/> member behaves distinctly when a job's next tick becomes
/// due while its previous execution is still in flight. The first invocation is deliberately held open
/// via <see cref="RecordingCommandRecorder.OnHandling"/> so the "still running" window is genuinely
/// observed by the second tick rather than assumed — never a real sleep standing in for coordination.
/// </summary>
public sealed class OverlapPolicyTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset FirstDue = Start.AddSeconds(1);
    private static readonly DateTimeOffset SecondDue = Start.AddSeconds(2);

    private static async Task<(SchedulingTestHarness Harness, TaskCompletionSource Release)> BuildWithFirstInvocationHeldAsync(OverlapPolicy overlapPolicy)
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var harness = SchedulingTestHarness.Build(
            registerJobs: builder => builder.AddRecurring<RecordingCommand>(
                "every-second-job",
                "* * * * * ?",
                _ => new RecordingCommand(),
                options =>
                {
                    options.MisfirePolicy = MisfirePolicy.Skip; // irrelevant here — never the thing under test
                    options.OverlapPolicy = overlapPolicy;
                }),
            initialClock: Start);

        harness.Recorder.OnHandling = async (index, ct) =>
        {
            if (index == 1)
            {
                await release.Task.WaitAsync(ct).ConfigureAwait(false);
            }
        };

        await harness.StartAsync();
        harness.Clock.Set(FirstDue);

        (await Eventually.UntilAsync(() => harness.Recorder.InvocationCount >= 1))
            .Should().BeTrue("the first invocation must start before the second tick can observe it as still running");

        harness.Clock.Set(SecondDue);

        return (harness, release);
    }

    [Fact]
    public async Task Skip_SecondTickNeverStarts_WhileFirstStillRunning()
    {
        (SchedulingTestHarness harness, TaskCompletionSource release) = await BuildWithFirstInvocationHeldAsync(OverlapPolicy.Skip);
        await using var _ = harness;

        (await Eventually.StaysAsync(() => harness.Recorder.InvocationCount == 1, TimeSpan.FromMilliseconds(300)))
            .Should().BeTrue("OverlapPolicy.Skip must discard the second tick entirely, not merely delay it");

        release.SetResult();
        await harness.StopAsync();
    }

    [Fact]
    public async Task Queue_SecondTickWaitsThenRunsImmediatelyAfterFirstCompletes()
    {
        (SchedulingTestHarness harness, TaskCompletionSource release) = await BuildWithFirstInvocationHeldAsync(OverlapPolicy.Queue);
        await using var _ = harness;

        (await Eventually.StaysAsync(() => harness.Recorder.InvocationCount == 1, TimeSpan.FromMilliseconds(300)))
            .Should().BeTrue("OverlapPolicy.Queue must not start the second execution while the first is still running");

        release.SetResult();

        (await Eventually.UntilAsync(() => harness.Recorder.InvocationCount == 2))
            .Should().BeTrue("the queued tick must run once the in-flight execution completes");

        harness.Recorder.MaxConcurrentObserved.Should().Be(1, "Queue guarantees at most one execution of this job at a time");

        await harness.StopAsync();
    }

    [Fact]
    public async Task Allow_SecondTickRunsConcurrentlyWithFirst()
    {
        (SchedulingTestHarness harness, TaskCompletionSource release) = await BuildWithFirstInvocationHeldAsync(OverlapPolicy.Allow);
        await using var _ = harness;

        (await Eventually.UntilAsync(() => harness.Recorder.InvocationCount >= 2))
            .Should().BeTrue("OverlapPolicy.Allow must start the second execution immediately, without waiting for the first");

        harness.Recorder.MaxConcurrentObserved.Should().BeGreaterThanOrEqualTo(2);

        release.SetResult();
        await harness.StopAsync();
    }
}

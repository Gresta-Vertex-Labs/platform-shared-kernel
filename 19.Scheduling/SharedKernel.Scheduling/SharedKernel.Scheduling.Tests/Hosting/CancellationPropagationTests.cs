using FluentAssertions;
using SharedKernel.Scheduling.Policies;
using SharedKernel.Scheduling.Tests.TestSupport;
using Xunit;

namespace SharedKernel.Scheduling.Tests.Hosting;

/// <summary>
/// Host shutdown propagates into an in-flight job rather than abandoning it: the same
/// <c>stoppingToken</c> the hosted loop observes is threaded all the way into the MediatR command
/// handler, and <c>SchedulingHostedService.StopAsync</c> waits for that in-flight execution to actually
/// finish (or observe cancellation) before returning — it does not simply walk away.
/// </summary>
public sealed class CancellationPropagationTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task StopAsync_WaitsForInFlightExecution_AndCommandObservesCancellation()
    {
        var handlerObservedCancellation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var handlerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await using SchedulingTestHarness harness = SchedulingTestHarness.Build(
            registerJobs: builder => builder.AddRecurring<RecordingCommand>(
                "long-running-job",
                "* * * * * ?",
                _ => new RecordingCommand(),
                options =>
                {
                    options.MisfirePolicy = MisfirePolicy.Skip;
                    options.OverlapPolicy = OverlapPolicy.Allow;
                }),
            initialClock: Start);

        harness.Recorder.OnHandling = async (_, ct) =>
        {
            handlerStarted.TrySetResult();
            try
            {
                // Waits until either genuinely cancelled (the behavior under test) or a generous
                // safety timeout that would only trip if cancellation was NOT propagated.
                await Task.Delay(Timeout.InfiniteTimeSpan, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                handlerObservedCancellation.TrySetResult(true);
                throw;
            }
        };

        await harness.StartAsync();
        harness.Clock.Set(Start.AddSeconds(1));

        (await Eventually.UntilAsync(() => harness.Recorder.InvocationCount >= 1))
            .Should().BeTrue();
        await handlerStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Trigger shutdown while the command handler is still in flight.
        using var stopCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        Task stopTask = harness.StopAsync(stopCts.Token);

        bool observedCancellation = await handlerObservedCancellation.Task.WaitAsync(TimeSpan.FromSeconds(5));
        observedCancellation.Should().BeTrue("the host's stopping token must propagate into the in-flight command handler");

        // StopAsync must not return before the in-flight execution has actually wound down.
        await stopTask;
    }
}

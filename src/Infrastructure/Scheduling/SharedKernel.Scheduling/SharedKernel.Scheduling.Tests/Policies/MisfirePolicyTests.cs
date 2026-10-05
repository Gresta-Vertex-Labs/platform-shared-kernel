using FluentAssertions;
using SharedKernel.Scheduling.Policies;
using SharedKernel.Scheduling.Tests.TestSupport;
using Xunit;

namespace SharedKernel.Scheduling.Tests.Policies;

/// <summary>
/// T-02 — each <see cref="MisfirePolicy"/> member behaves distinctly after a simulated downtime
/// window, driven entirely by <see cref="SharedKernel.Testing.Clocks.FakeClock"/> jumps — never a real
/// sleep standing in for elapsed time. All three tests use the identical setup (a recurring job firing
/// every minute of simulated time, downtime spanning exactly 12 missed occurrences) so the expected
/// invocation counts below are a direct, mechanical consequence of each policy's documented behavior
/// (see <see cref="MisfirePolicy"/>'s own remarks) rather than tuned to make the test pass.
/// </summary>
public sealed class MisfirePolicyTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 30, TimeSpan.Zero);

    // First due-at is 00:01:00 (the next whole-minute boundary after Start). Jumping 12 minutes
    // past Start (to 00:12:30) leaves exactly the minute boundaries 00:01 through 00:12 — 12
    // occurrences — behind "now".
    private static readonly DateTimeOffset AfterSimulatedDowntime = Start.AddMinutes(12);

    private static async Task<SchedulingTestHarness> BuildAndRunPastDowntimeAsync(MisfirePolicy misfirePolicy)
    {
        var harness = SchedulingTestHarness.Build(
            registerJobs: builder => builder.AddRecurring<RecordingCommand>(
                "minutely-job",
                "0 * * * * ?",
                _ => new RecordingCommand(),
                options =>
                {
                    options.MisfirePolicy = misfirePolicy;
                    options.OverlapPolicy = OverlapPolicy.Allow; // irrelevant here — never the thing under test
                }),
            initialClock: Start);

        await harness.StartAsync();
        harness.Clock.Set(AfterSimulatedDowntime);
        return harness;
    }

    [Fact]
    public async Task Skip_DiscardsEveryMissedOccurrence_NeverFires()
    {
        await using SchedulingTestHarness harness = await BuildAndRunPastDowntimeAsync(MisfirePolicy.Skip);

        // Give the loop ample real time to process every missed tick, then assert the count never
        // moved off zero and stays there.
        (await Eventually.StaysAsync(() => harness.Recorder.InvocationCount == 0, TimeSpan.FromMilliseconds(500)))
            .Should().BeTrue();

        await harness.StopAsync();
    }

    [Fact]
    public async Task FireOnce_CollapsesAllMissedOccurrencesIntoExactlyOneRun()
    {
        await using SchedulingTestHarness harness = await BuildAndRunPastDowntimeAsync(MisfirePolicy.FireOnce);

        (await Eventually.UntilAsync(() => harness.Recorder.InvocationCount >= 1)).Should().BeTrue();

        // Must stay at exactly 1 — never replay the other 11 missed occurrences.
        (await Eventually.StaysAsync(() => harness.Recorder.InvocationCount == 1, TimeSpan.FromMilliseconds(500)))
            .Should().BeTrue();

        await harness.StopAsync();
    }

    [Fact]
    public async Task RunImmediatelyThenReschedule_ReplaysEveryMissedOccurrenceOnePerTick()
    {
        await using SchedulingTestHarness harness = await BuildAndRunPastDowntimeAsync(MisfirePolicy.RunImmediatelyThenReschedule);

        (await Eventually.UntilAsync(() => harness.Recorder.InvocationCount >= 12, timeout: TimeSpan.FromSeconds(10)))
            .Should().BeTrue("all 12 missed minute-boundaries should eventually be replayed");

        // Must stabilize at exactly 12 — the schedule has caught up to "now" and the next due time
        // (00:13:00) is still in the future relative to the clock, which was never advanced further.
        (await Eventually.StaysAsync(() => harness.Recorder.InvocationCount == 12, TimeSpan.FromMilliseconds(500)))
            .Should().BeTrue();

        await harness.StopAsync();
    }
}

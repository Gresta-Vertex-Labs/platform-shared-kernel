using System.Diagnostics;
using FluentAssertions;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Propagation;
using SharedKernel.Primitives.Results;
using SharedKernel.Primitives.Telemetry;
using SharedKernel.Scheduling.Diagnostics;
using SharedKernel.Scheduling.Policies;
using SharedKernel.Scheduling.Tests.TestSupport;
using SharedKernel.Testing.Caching;
using SharedKernel.Testing.Telemetry;
using Xunit;

namespace SharedKernel.Scheduling.Tests.Diagnostics;

/// <summary>
/// Pins the telemetry contract of the package: instrument names, units and kinds, the tag sets, the single
/// <c>outcome</c> vocabulary, the separate <c>scheduling.claim</c> tag, the span shape and the counts every path
/// produces. Every scenario uses a unique job name and filters on it, because the recorders listen process-wide.
/// </summary>
public sealed class SchedulingTelemetryContractTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset FirstDue = Start.AddSeconds(1);
    private static readonly DateTimeOffset SecondDue = Start.AddSeconds(2);
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    private static string UniqueName(string prefix) => $"{prefix}-{Guid.NewGuid():N}";

    private static KeyValuePair<string, object?> Job(string jobName) => new(SchedulingTagKeys.JobName, jobName);

    private static KeyValuePair<string, object?> Outcome(string outcome) => new(WellKnownTagKeys.Outcome, outcome);

    private static SchedulingTestHarness Build(
        string jobName,
        IDistributedLockService? lockService = null,
        OverlapPolicy overlap = OverlapPolicy.Skip,
        MisfirePolicy misfire = MisfirePolicy.Skip,
        string cron = "* * * * * ?",
        DateTimeOffset? initial = null) =>
        SchedulingTestHarness.Build(
            registerJobs: builder => builder.AddRecurring<RecordingCommand>(
                jobName,
                cron,
                _ => new RecordingCommand(),
                options =>
                {
                    options.MisfirePolicy = misfire;
                    options.OverlapPolicy = overlap;
                }),
            initialClock: initial ?? Start,
            lockService: lockService);

    private static Activity? SpanOf(ActivityRecorder recorder, string jobName) =>
        recorder.RecordedActivities.FirstOrDefault(a => string.Equals(a.GetTagItem(SchedulingTagKeys.JobName) as string, jobName, StringComparison.Ordinal));

    private static async Task<Activity> WaitForSpanAsync(ActivityRecorder recorder, string jobName)
    {
        (await Eventually.UntilAsync(() => SpanOf(recorder, jobName) is not null, Wait)).Should().BeTrue("the fire span must stop");
        return SpanOf(recorder, jobName)!;
    }

    // ---- Instrument contract ---------------------------------------------------------------------------------

    [Fact]
    public async Task Instruments_AreNamedUnitedAndTypedLiterally_AndFollowTheMetricConvention()
    {
        using MetricRecorder metrics = MetricRecorder.StartRecording(SchedulingTelemetry.Name);
        await using SchedulingTestHarness harness = Build(UniqueName("instruments"));

        metrics
            .ShouldHaveInstrument("sharedkernel.scheduling.job.executions", "{execution}", InstrumentKind.Counter)
            .ShouldHaveInstrument("sharedkernel.scheduling.job.duration", "s", InstrumentKind.Histogram)
            .ShouldHaveInstrument("sharedkernel.scheduling.job.overlap_skipped", "{occurrence}", InstrumentKind.Counter)
            .ShouldHaveInstrument("sharedkernel.scheduling.job.overlap_queued", "{occurrence}", InstrumentKind.Counter)
            .ShouldHaveInstrument("sharedkernel.scheduling.job.misfired", "{occurrence}", InstrumentKind.Counter)
            .ShouldHaveInstrument("sharedkernel.scheduling.lock.not_acquired", "{claim}", InstrumentKind.Counter)
            .ShouldHaveInstrument("sharedkernel.scheduling.lock.store_unavailable", "{claim}", InstrumentKind.Counter)
            .ShouldFollowMetricConvention();
        metrics.Instruments.Where(i => i.MeterName == SchedulingTelemetry.Name).Select(i => i.Name).Distinct().Should().HaveCount(7);
    }

    // ---- Executions and duration -----------------------------------------------------------------------------

    private static async Task<(MetricRecorder Metrics, ActivityRecorder Spans)> RunSingleFireAsync(
        string jobName,
        Action<SchedulingTestHarness> arrange,
        IDistributedLockService? lockService,
        Func<SchedulingTestHarness, Task> waitForFire,
        bool stopBeforeWait = false)
    {
        MetricRecorder metrics = MetricRecorder.StartRecording(SchedulingTelemetry.Name);
        ActivityRecorder spans = ActivityRecorder.StartRecording(SchedulingTelemetry.Name);
        await using SchedulingTestHarness harness = Build(jobName, lockService);
        arrange(harness);

        await harness.StartAsync();
        harness.Clock.Set(FirstDue);
        if (stopBeforeWait)
        {
            (await Eventually.UntilAsync(() => harness.Recorder.InvocationCount == 1, Wait)).Should().BeTrue();
            await harness.StopAsync();
        }

        await waitForFire(harness);
        await WaitForSpanAsync(spans, jobName);
        if (!stopBeforeWait)
        {
            await harness.StopAsync();
        }

        return (metrics, spans);
    }

    [Fact]
    public async Task Success_AddsOneExecutionWithOutcomeSuccess_AndOneDurationFromTheClock()
    {
        string jobName = UniqueName("success");
        (MetricRecorder metrics, ActivityRecorder spans) = await RunSingleFireAsync(
            jobName,
            harness => harness.Recorder.OnHandling = (_, _) =>
            {
                harness.Clock.Advance(TimeSpan.FromSeconds(2));
                return Task.CompletedTask;
            },
            new FakeDistributedLockService(),
            harness => Eventually.UntilAsync(() => harness.Recorder.InvocationCount == 1, Wait));
        using (metrics)
        using (spans)
        {
            (await Eventually.UntilAsync(() => metrics.Count(SchedulingMetrics.ExecutionsName, Job(jobName)) == 1, Wait)).Should().BeTrue();

            metrics.Count(SchedulingMetrics.ExecutionsName, Job(jobName), Outcome(WellKnownOutcomes.Success)).Should().Be(1);
            metrics.Count(SchedulingMetrics.DurationName, Job(jobName), Outcome(WellKnownOutcomes.Success)).Should().Be(1);
            metrics.Sum(SchedulingMetrics.DurationName, Job(jobName)).Should().Be(2d, "the handler advanced FakeClock by two seconds");
            metrics.ShouldHaveTagSet(SchedulingMetrics.ExecutionsName, SchedulingTagKeys.JobName, WellKnownTagKeys.Outcome);
            metrics.ShouldHaveTagSet(SchedulingMetrics.DurationName, SchedulingTagKeys.JobName, WellKnownTagKeys.Outcome);
            metrics.ShouldUseOutcomeVocabulary(SchedulingMetrics.ExecutionsName);
            metrics.ShouldUseOutcomeVocabulary(SchedulingMetrics.DurationName);
            metrics.ShouldNotHaveTagKey(SchedulingMetrics.ExecutionsName, "scheduling.outcome");
            metrics.Count(SchedulingMetrics.LockNotAcquiredName, Job(jobName)).Should().Be(0);
        }
    }

    [Fact]
    public async Task FailedResult_AddsOneExecutionWithOutcomeFailure()
    {
        string jobName = UniqueName("failed");
        (MetricRecorder metrics, ActivityRecorder spans) = await RunSingleFireAsync(
            jobName,
            harness => harness.Recorder.ResultFactory = () => Result.Failure(Error.Conflict("scheduling.test.conflict", "conflict")),
            lockService: null,
            harness => Eventually.UntilAsync(() => harness.Recorder.InvocationCount == 1, Wait));
        using (metrics)
        using (spans)
        {
            (await Eventually.UntilAsync(() => metrics.Count(SchedulingMetrics.ExecutionsName, Job(jobName)) == 1, Wait)).Should().BeTrue();

            metrics.Count(SchedulingMetrics.ExecutionsName, Job(jobName), Outcome(WellKnownOutcomes.Failure)).Should().Be(1);
            metrics.Count(SchedulingMetrics.DurationName, Job(jobName), Outcome(WellKnownOutcomes.Failure)).Should().Be(1);
        }
    }

    [Fact]
    public async Task ThrownException_AddsOneExecutionWithOutcomeException()
    {
        string jobName = UniqueName("thrown");
        (MetricRecorder metrics, ActivityRecorder spans) = await RunSingleFireAsync(
            jobName,
            harness => harness.Recorder.OnHandling = (_, _) => throw new InvalidOperationException("boom"),
            lockService: null,
            harness => Eventually.UntilAsync(() => harness.Recorder.InvocationCount == 1, Wait));
        using (metrics)
        using (spans)
        {
            (await Eventually.UntilAsync(() => metrics.Count(SchedulingMetrics.ExecutionsName, Job(jobName)) == 1, Wait)).Should().BeTrue();

            metrics.Count(SchedulingMetrics.ExecutionsName, Job(jobName), Outcome(WellKnownOutcomes.Exception)).Should().Be(1);
            metrics.Count(SchedulingMetrics.DurationName, Job(jobName), Outcome(WellKnownOutcomes.Exception)).Should().Be(1);
        }
    }

    [Fact]
    public async Task ShutdownCancellation_AddsOneExecutionWithOutcomeCanceled()
    {
        string jobName = UniqueName("canceled");
        (MetricRecorder metrics, ActivityRecorder spans) = await RunSingleFireAsync(
            jobName,
            harness => harness.Recorder.OnHandling = (_, ct) => Task.Delay(Timeout.InfiniteTimeSpan, ct),
            lockService: null,
            _ => Task.CompletedTask,
            stopBeforeWait: true);
        using (metrics)
        using (spans)
        {
            (await Eventually.UntilAsync(() => metrics.Count(SchedulingMetrics.ExecutionsName, Job(jobName)) == 1, Wait)).Should().BeTrue();

            metrics.Count(SchedulingMetrics.ExecutionsName, Job(jobName), Outcome(WellKnownOutcomes.Canceled)).Should().Be(1);
            metrics.Count(SchedulingMetrics.DurationName, Job(jobName), Outcome(WellKnownOutcomes.Canceled)).Should().Be(1);
        }
    }

    [Fact]
    public async Task LostClaim_AddsLockNotAcquired_AndNeitherAnExecutionNorADuration()
    {
        string jobName = UniqueName("lost");
        (MetricRecorder metrics, ActivityRecorder spans) = await RunSingleFireAsync(
            jobName,
            _ => { },
            new FakeDistributedLockService { SimulateContention = true },
            _ => Task.CompletedTask);
        using (metrics)
        using (spans)
        {
            (await Eventually.UntilAsync(() => metrics.Count(SchedulingMetrics.LockNotAcquiredName, Job(jobName)) == 1, Wait)).Should().BeTrue();

            metrics.Count(SchedulingMetrics.ExecutionsName, Job(jobName)).Should().Be(0);
            metrics.Count(SchedulingMetrics.DurationName, Job(jobName)).Should().Be(0);
            metrics.Count(SchedulingMetrics.LockStoreUnavailableName, Job(jobName)).Should().Be(0);
        }
    }

    [Fact]
    public async Task LockOutage_AddsLockStoreUnavailable_AndNeitherAnExecutionNorADuration()
    {
        string jobName = UniqueName("outage");
        (MetricRecorder metrics, ActivityRecorder spans) = await RunSingleFireAsync(
            jobName,
            _ => { },
            new FakeDistributedLockService { SimulateUnavailable = true },
            _ => Task.CompletedTask);
        using (metrics)
        using (spans)
        {
            (await Eventually.UntilAsync(() => metrics.Count(SchedulingMetrics.LockStoreUnavailableName, Job(jobName)) == 1, Wait)).Should().BeTrue();

            metrics.Count(SchedulingMetrics.ExecutionsName, Job(jobName)).Should().Be(0);
            metrics.Count(SchedulingMetrics.DurationName, Job(jobName)).Should().Be(0);
            metrics.Count(SchedulingMetrics.LockNotAcquiredName, Job(jobName)).Should().Be(0);
        }
    }

    // ---- Overlap and misfire counters ------------------------------------------------------------------------

    [Theory]
    [InlineData(OverlapPolicy.Skip)]
    [InlineData(OverlapPolicy.Queue)]
    public async Task Overlap_CountsTheSkippedOrQueuedOccurrence_UnderTheNewNames(OverlapPolicy policy)
    {
        string jobName = UniqueName("overlap");
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using MetricRecorder metrics = MetricRecorder.StartRecording(SchedulingTelemetry.Name);
        await using SchedulingTestHarness harness = Build(jobName, overlap: policy);
        harness.Recorder.OnHandling = async (index, ct) =>
        {
            if (index == 1)
            {
                await release.Task.WaitAsync(ct).ConfigureAwait(false);
            }
        };

        await harness.StartAsync();
        harness.Clock.Set(FirstDue);
        (await Eventually.UntilAsync(() => harness.Recorder.InvocationCount == 1, Wait)).Should().BeTrue();
        harness.Clock.Set(SecondDue);

        string counted = policy == OverlapPolicy.Skip ? SchedulingMetrics.OverlapSkippedName : SchedulingMetrics.OverlapQueuedName;
        string other = policy == OverlapPolicy.Skip ? SchedulingMetrics.OverlapQueuedName : SchedulingMetrics.OverlapSkippedName;
        (await Eventually.UntilAsync(() => metrics.Count(counted, Job(jobName)) == 1, Wait)).Should().BeTrue();
        metrics.Count(other, Job(jobName)).Should().Be(0);

        release.SetResult();
        await harness.StopAsync();
    }

    [Theory]
    [InlineData(MisfirePolicy.Skip)]
    [InlineData(MisfirePolicy.FireOnce)]
    [InlineData(MisfirePolicy.RunImmediatelyThenReschedule)]
    public async Task Misfire_CountsTheDetectedMisfire_UnderTheNewName(MisfirePolicy policy)
    {
        string jobName = UniqueName("misfire");
        using MetricRecorder metrics = MetricRecorder.StartRecording(SchedulingTelemetry.Name);
        await using SchedulingTestHarness harness = Build(jobName, misfire: policy, cron: "0 * * * * ?", initial: Start.AddSeconds(30));

        await harness.StartAsync();
        harness.Clock.Set(Start.AddMinutes(12));

        (await Eventually.UntilAsync(() => metrics.Count(SchedulingMetrics.MisfiredName, Job(jobName)) >= 1, Wait)).Should().BeTrue();
        await harness.StopAsync();
    }

    // ---- Spans -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task SuccessSpan_HasTheContractShape_AndTheSourceVersion()
    {
        string jobName = UniqueName("span-success");
        (MetricRecorder metrics, ActivityRecorder spans) = await RunSingleFireAsync(
            jobName, _ => { }, new FakeDistributedLockService(), harness => Eventually.UntilAsync(() => harness.Recorder.InvocationCount == 1, Wait));
        using (metrics)
        using (spans)
        {
            Activity span = SpanOf(spans, jobName)!;

            span.ShouldFollowSpanConvention();
            span.OperationName.Should().Be("scheduling fire");
            span.ShouldHaveKind(ActivityKind.Internal)
                .ShouldHaveSource(SchedulingTelemetry.Name)
                .ShouldHaveTagKeys(
                    SchedulingTagKeys.JobName,
                    SchedulingTagKeys.MisfirePolicy,
                    SchedulingTagKeys.OverlapPolicy,
                    SchedulingTagKeys.Claim,
                    WellKnownTagKeys.Outcome)
                .ShouldHaveTag(SchedulingTagKeys.Claim, SchedulingClaimResults.Acquired)
                .ShouldHaveOutcome(WellKnownOutcomes.Success)
                .ShouldBeUnset();
            span.Source.Version.Should().Be(TelemetryVersion.Of(typeof(SchedulingTelemetry).Assembly));
        }
    }

    [Fact]
    public async Task SpanWithoutALockService_HasNoClaimTag()
    {
        string jobName = UniqueName("span-nolock");
        (MetricRecorder metrics, ActivityRecorder spans) = await RunSingleFireAsync(
            jobName, _ => { }, lockService: null, harness => Eventually.UntilAsync(() => harness.Recorder.InvocationCount == 1, Wait));
        using (metrics)
        using (spans)
        {
            SpanOf(spans, jobName)!
                .ShouldNotHaveTag(SchedulingTagKeys.Claim)
                .ShouldHaveOutcome(WellKnownOutcomes.Success);
        }
    }

    [Fact]
    public async Task FailedResultSpan_HasOutcomeFailure_AndTheRecordedError()
    {
        string jobName = UniqueName("span-failed");
        Error error = Error.Conflict("scheduling.test.conflict", "conflict");
        (MetricRecorder metrics, ActivityRecorder spans) = await RunSingleFireAsync(
            jobName,
            harness => harness.Recorder.ResultFactory = () => Result.Failure(error),
            lockService: null,
            harness => Eventually.UntilAsync(() => harness.Recorder.InvocationCount == 1, Wait));
        using (metrics)
        using (spans)
        {
            Activity span = SpanOf(spans, jobName)!;

            span.ShouldHaveOutcome(WellKnownOutcomes.Failure).ShouldBeRecordedFailure(error);
        }
    }

    [Fact]
    public async Task ThrownSpan_HasOutcomeException()
    {
        string jobName = UniqueName("span-thrown");
        (MetricRecorder metrics, ActivityRecorder spans) = await RunSingleFireAsync(
            jobName,
            harness => harness.Recorder.OnHandling = (_, _) => throw new InvalidOperationException("boom"),
            lockService: null,
            harness => Eventually.UntilAsync(() => harness.Recorder.InvocationCount == 1, Wait));
        using (metrics)
        using (spans)
        {
            SpanOf(spans, jobName)!
                .ShouldHaveOutcome(WellKnownOutcomes.Exception)
                .ShouldBeRecordedException(typeof(InvalidOperationException));
        }
    }

    [Fact]
    public async Task CanceledSpan_HasOutcomeCanceled_AndNoErrorStatus()
    {
        string jobName = UniqueName("span-canceled");
        (MetricRecorder metrics, ActivityRecorder spans) = await RunSingleFireAsync(
            jobName,
            harness => harness.Recorder.OnHandling = (_, ct) => Task.Delay(Timeout.InfiniteTimeSpan, ct),
            lockService: null,
            _ => Task.CompletedTask,
            stopBeforeWait: true);
        using (metrics)
        using (spans)
        {
            SpanOf(spans, jobName)!
                .ShouldHaveOutcome(WellKnownOutcomes.Canceled)
                .ShouldBeRecordedCancellation();
        }
    }

    [Fact]
    public async Task LostClaimSpan_HasTheClaimTag_AndNoOutcome()
    {
        string jobName = UniqueName("span-lost");
        (MetricRecorder metrics, ActivityRecorder spans) = await RunSingleFireAsync(
            jobName, _ => { }, new FakeDistributedLockService { SimulateContention = true }, _ => Task.CompletedTask);
        using (metrics)
        using (spans)
        {
            SpanOf(spans, jobName)!
                .ShouldHaveTag(SchedulingTagKeys.Claim, SchedulingClaimResults.NotAcquired)
                .ShouldNotHaveTag(WellKnownTagKeys.Outcome)
                .ShouldBeUnset();
        }
    }

    [Fact]
    public async Task LockOutageSpan_HasTheClaimTag_AndOutcomeException()
    {
        string jobName = UniqueName("span-outage");
        (MetricRecorder metrics, ActivityRecorder spans) = await RunSingleFireAsync(
            jobName, _ => { }, new FakeDistributedLockService { SimulateUnavailable = true }, _ => Task.CompletedTask);
        using (metrics)
        using (spans)
        {
            SpanOf(spans, jobName)!
                .ShouldHaveTag(SchedulingTagKeys.Claim, SchedulingClaimResults.StoreUnavailable)
                .ShouldHaveOutcome(WellKnownOutcomes.Exception)
                .ShouldBeRecordedException(typeof(DistributedLockUnavailableException), "caching.lock_unavailable");
        }
    }
}

using System.Collections.Concurrent;
using System.Diagnostics;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Primitives.Logging;
using SharedKernel.Scheduling.Diagnostics;
using SharedKernel.Scheduling.Policies;
using SharedKernel.Scheduling.Tests.TestSupport;
using SharedKernel.Testing.Caching;
using SharedKernel.Testing.Logging;
using Xunit;

namespace SharedKernel.Scheduling.Tests.Locking;

/// <summary>
/// The per-occurrence lease that guards cross-replica single execution: a contended lease and an
/// unreachable lock store both skip the occurrence, an acquired lease hands its fencing token to the
/// job, and the lease is never released after the job finishes.
/// </summary>
public sealed class OccurrenceLeaseTests
{
    private const int LockAcquisitionFailedEventId = LoggingEventIdRanges.Scheduling + 11;
    private const int LockStoreUnavailableEventId = LoggingEventIdRanges.Scheduling + 16;

    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset FirstDue = Start.AddSeconds(1);
    private static readonly DateTimeOffset SecondDue = Start.AddSeconds(2);

    private static SchedulingTestHarness BuildHarness(
        string jobName,
        IDistributedLockService lockService,
        ConcurrentQueue<long?>? observedFencingTokens = null) =>
        SchedulingTestHarness.Build(
            registerJobs: builder => builder.AddRecurring<RecordingCommand>(
                jobName,
                "* * * * * ?",
                context =>
                {
                    observedFencingTokens?.Enqueue(context.FencingToken);
                    return new RecordingCommand();
                },
                options =>
                {
                    options.MisfirePolicy = MisfirePolicy.Skip;
                    options.OverlapPolicy = OverlapPolicy.Skip;
                }),
            initialClock: Start,
            lockService: lockService);

    private static string UniqueJobName(string prefix) => $"{prefix}-{Guid.NewGuid():N}";

    private static bool HasLog(SchedulingTestHarness harness, int eventId) =>
        harness.HostedServiceLogRecords.Any(record => record.EventId.Id == eventId);

    [Fact]
    public async Task ContendedLease_JobNotExecuted_LogsLockAcquisitionFailed()
    {
        string jobName = UniqueJobName("contended");
        var lockService = new FakeDistributedLockService { SimulateContention = true };
        using var outcomes = new OutcomeRecorder(jobName);
        await using SchedulingTestHarness harness = BuildHarness(jobName, lockService);

        await harness.StartAsync();
        harness.Clock.Set(FirstDue);

        (await Eventually.UntilAsync(() => HasLog(harness, LockAcquisitionFailedEventId)))
            .Should().BeTrue("a contended occurrence must be reported as claimed by another replica");
        (await Eventually.StaysAsync(() => harness.Recorder.InvocationCount == 0, TimeSpan.FromMilliseconds(300)))
            .Should().BeTrue("a replica that did not win the lease must never run the job");

        HasLog(harness, LockStoreUnavailableEventId).Should().BeFalse();
        (await Eventually.UntilAsync(() => outcomes.Contains("lock-not-acquired"))).Should().BeTrue();

        await harness.StopAsync();
    }

    [Fact]
    public async Task LockStoreUnavailable_JobNotExecuted_LogsError_AndSchedulerKeepsRunning()
    {
        string jobName = UniqueJobName("store-down");
        var lockService = new FakeDistributedLockService { SimulateUnavailable = true };
        using var outcomes = new OutcomeRecorder(jobName);
        await using SchedulingTestHarness harness = BuildHarness(jobName, lockService);

        await harness.StartAsync();
        harness.Clock.Set(FirstDue);

        (await Eventually.UntilAsync(() => HasLog(harness, LockStoreUnavailableEventId)))
            .Should().BeTrue("an unreachable lock store must be logged as its own error");

        LogRecord record = harness.HostedServiceLogRecords.Single(r => r.EventId.Id == LockStoreUnavailableEventId);
        record.Exception.Should().BeOfType<DistributedLockUnavailableException>();
        record.TryGetProperty("JobName", out object? loggedJobName).Should().BeTrue();
        loggedJobName.Should().Be(jobName);

        harness.Recorder.InvocationCount.Should().Be(0, "no replica may run an occurrence it cannot prove it owns");
        HasLog(harness, LockAcquisitionFailedEventId).Should().BeFalse("an outage must never be reported as another replica's claim");
        (await Eventually.UntilAsync(() => outcomes.Contains("lock-store-unavailable"))).Should().BeTrue();

        // The store recovers: the next occurrence runs normally.
        lockService.SimulateUnavailable = false;
        harness.Clock.Set(SecondDue);

        (await Eventually.UntilAsync(() => harness.Recorder.InvocationCount == 1))
            .Should().BeTrue("the scheduler loop must survive a lock-store outage and fire the next occurrence");
        harness.HostedService.IsRunning.Should().BeTrue();

        await harness.StopAsync();
    }

    [Fact]
    public async Task AcquiredLease_JobRunsWithTheLeasesFencingToken()
    {
        string jobName = UniqueJobName("fenced");
        var lockService = Substitute.For<IDistributedLockService>();
        lockService
            .TryAcquireLeaseAsync(Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(call => ValueTask.FromResult<DistributedLease?>(
                new DistributedLease(call.ArgAt<string>(0), 4242, Start.AddMinutes(5))));
        lockService
            .TryAcquireAsync(Arg.Any<string>(), Arg.Any<DistributedLockOptions?>(), Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("The scheduler takes a lease per occurrence, never a lock."));
        var observedTokens = new ConcurrentQueue<long?>();
        await using SchedulingTestHarness harness = BuildHarness(jobName, lockService, observedTokens);

        await harness.StartAsync();
        harness.Clock.Set(FirstDue);

        (await Eventually.UntilAsync(() => harness.Recorder.InvocationCount == 1)).Should().BeTrue();

        observedTokens.Should().Equal(4242L);
        await lockService.Received(1).TryAcquireLeaseAsync(
            Arg.Is<string>(resource => resource.Contains(jobName, StringComparison.Ordinal)),
            TimeSpan.FromMinutes(5),
            Arg.Any<CancellationToken>());

        await harness.StopAsync();
    }

    [Fact]
    public async Task OccurrenceLease_IsNeverReleased_SecondReplicaForSameOccurrenceGetsNothing()
    {
        string jobName = UniqueJobName("never-released");
        var lockService = new FakeDistributedLockService();
        var observedTokens = new ConcurrentQueue<long?>();

        await using SchedulingTestHarness replicaA = BuildHarness(jobName, lockService, observedTokens);
        await replicaA.StartAsync();
        replicaA.Clock.Set(FirstDue);

        (await Eventually.UntilAsync(() => replicaA.Recorder.InvocationCount == 1)).Should().BeTrue();
        await replicaA.StopAsync();

        DistributedLease lease = lockService.AcquiredLeases.Should().ContainSingle().Subject;
        lease.Resource.Should().Contain(jobName);
        observedTokens.Should().Equal(lease.FencingToken);

        // The job has finished, yet the occurrence is still claimed.
        (await lockService.TryAcquireLeaseAsync(lease.Resource, TimeSpan.FromMinutes(1)))
            .Should().BeNull("releasing the lease after the job would let a late replica run the same occurrence again");

        await using SchedulingTestHarness replicaB = BuildHarness(jobName, lockService);
        await replicaB.StartAsync();
        replicaB.Clock.Set(FirstDue);

        (await Eventually.UntilAsync(() => HasLog(replicaB, LockAcquisitionFailedEventId))).Should().BeTrue();
        replicaB.Recorder.InvocationCount.Should().Be(0);
        lockService.AcquiredLeases.Should().ContainSingle();

        await replicaB.StopAsync();
    }

    /// <summary>Collects the outcome tag of every stopped fire activity for one job.</summary>
    private sealed class OutcomeRecorder : IDisposable
    {
        private readonly ActivityListener _listener;
        private readonly ConcurrentQueue<string> _outcomes = new();

        public OutcomeRecorder(string jobName)
        {
            _listener = new ActivityListener
            {
                ShouldListenTo = source => source.Name == SchedulingTelemetry.Name,
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = activity =>
                {
                    if (activity.GetTagItem(SchedulingTagKeys.JobName) as string == jobName
                        && activity.GetTagItem(SchedulingTagKeys.Outcome) is string outcome)
                    {
                        _outcomes.Enqueue(outcome);
                    }
                },
            };
            ActivitySource.AddActivityListener(_listener);
        }

        public bool Contains(string outcome) => _outcomes.Contains(outcome);

        public void Dispose() => _listener.Dispose();
    }
}

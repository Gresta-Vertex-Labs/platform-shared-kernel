using System.Diagnostics;
using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Testing.Caching;
using SharedKernel.Execution.Context;
using SharedKernel.Execution.Tenancy;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Logging;
using SharedKernel.Primitives.Results;
using SharedKernel.Scheduling.Diagnostics;
using SharedKernel.Scheduling.Options;
using SharedKernel.Scheduling.Policies;
using SharedKernel.Scheduling.Tests.TestSupport;
using SharedKernel.Testing.Logging;
using SharedKernel.Testing.Telemetry;
using Xunit;

namespace SharedKernel.Scheduling.Tests.Hosting;

/// <summary>
/// Every scheduler log runs under a system caller (the job's for an occurrence, <c>scheduler</c> for the loop),
/// span status and logs carry codes and types but never an error or exception message, and no ambient context
/// survives on the caller of <c>StartAsync</c>/<c>StopAsync</c> or after a fire. The lock-outage double here
/// is not the multi-replica proof; that lives in <c>MultiReplicaSingleExecutionTests</c> over real Redis.
/// </summary>
public sealed class SchedulerLogContextTests
{
    private const string LoopIdentity = "scheduler";
    private const string LockUnavailableCode = "caching.lock_unavailable";

    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset FirstDue = Start.AddSeconds(1);
    private static readonly DateTimeOffset SecondDue = Start.AddSeconds(2);
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    private static readonly int[] LoopEventIds = [19000, 19001, 19002, 19007, 19008, 19009, 19010, 19013, 19015, 19017];
    private static readonly int[] OccurrenceEventIds = [19003, 19004, 19005, 19006, 19011, 19012, 19014, 19016];

    private static readonly (int Id, LogLevel Level, string Template)[] ExpectedLogs =
    [
        (19000, LogLevel.Information, "Scheduling started with {RegisteredJobCount} registered jobs."),
        (19001, LogLevel.Information, "Scheduling is stopping."),
        (19002, LogLevel.Warning, "No IDistributedLockService is registered, so every job runs once per replica; register one before running more than one replica."),
        (19003, LogLevel.Debug, "Job '{JobName}' is firing the occurrence scheduled at {ScheduledFireTimeUtc}, observed at {ActualFireTimeUtc}."),
        (19004, LogLevel.Debug, "Job '{JobName}' running '{RequestType}' succeeded in {ElapsedMilliseconds} ms."),
        (19005, LogLevel.Warning, "Job '{JobName}' running '{RequestType}' failed in {ElapsedMilliseconds} ms with {ErrorType} '{ErrorCode}'."),
        (19006, LogLevel.Error, "Job '{JobName}' threw an unhandled exception."),
        (19007, LogLevel.Warning, "Job '{JobName}' skipped an occurrence because the previous execution is still running."),
        (19008, LogLevel.Debug, "Job '{JobName}' queued an occurrence behind the running execution, with {QueueDepth} queued."),
        (19009, LogLevel.Warning, "Job '{JobName}' missed the occurrence due at {MissedFireTimeUtc}, first observed at {ObservedAtUtc}, under the {MisfirePolicy} policy."),
        (19010, LogLevel.Warning, "Job '{JobName}' discarded the missed occurrence due at {MissedFireTimeUtc}."),
        (19011, LogLevel.Debug, "Job '{JobName}' did not claim the occurrence because another replica already did."),
        (19012, LogLevel.Debug, "Job '{JobName}' claimed the occurrence with fencing token {FencingToken}."),
        (19013, LogLevel.Critical, "Scheduling loop faulted and is stopping."),
        (19014, LogLevel.Information, "Job '{JobName}' observed shutdown cancellation."),
        (19015, LogLevel.Warning, "Shutdown drain timed out while waiting for {InFlightCount} in-flight executions."),
        (19016, LogLevel.Error, "Job '{JobName}' skipped the occurrence because the lock store is unavailable, so no replica can claim it."),
        (19017, LogLevel.Error, "Shutdown drain faulted while waiting for in-flight executions."),
    ];

    // ---- Matrix ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task Matrix_EveryLogRunsUnderASystemCaller_AndEveryDeclaredEventIdIsExercised()
    {
        TenantId tenant = new(Guid.NewGuid());
        ScenarioResult[] scenarios =
        [
            await SuccessAsync(tenant),
            await FailedResultAsync("marker-failed-result"),
            await ThrownAsync("marker-thrown"),
            await CancelledAsync(),
            await LostClaimAsync(),
            await LockOutageAsync(),
            await MisfireAsync(),
            await OverlapAsync(OverlapPolicy.Skip),
            await OverlapAsync(OverlapPolicy.Queue),
            await FaultedLoopAsync(),
            await DrainTimeoutAsync(),
            await DrainFaultedAsync(),
        ];

        AmbientLogRecord[] all = [.. scenarios.SelectMany(s => s.Ambient)];

        all.Should().NotBeEmpty();
        all.Where(r => !r.HasContext).Should().BeEmpty("every scheduler log runs under a system caller");

        foreach (ScenarioResult scenario in scenarios)
        {
            foreach (AmbientLogRecord record in scenario.Ambient)
            {
                int id = record.EventId.Id;
                if (Array.IndexOf(LoopEventIds, id) >= 0)
                {
                    record.Identity.Should().Be(LoopIdentity, $"EventId {id} is a loop-level log");
                    record.TenantId.Should().BeNull();
                }
                else
                {
                    Array.IndexOf(OccurrenceEventIds, id).Should().BeGreaterThanOrEqualTo(0, $"EventId {id} must be classified as loop-level or occurrence-level");
                    record.Identity.Should().Be(scenario.JobName, $"EventId {id} is an occurrence log");
                    record.CorrelationId.Should().NotBeNullOrWhiteSpace();
                }
            }
        }

        HashSet<int> exercised = [.. all.Select(r => r.EventId.Id)];
        HashSet<int> declared = DeclaredEventIds();
        declared.Should().BeEquivalentTo(
            exercised,
            "a new EventId must come with a scenario in this matrix (and a loop or occurrence classification)");
        declared.Should().BeEquivalentTo(new HashSet<int>([.. LoopEventIds, .. OccurrenceEventIds]));

        LogRecord[] schedulerRecords = [.. scenarios.SelectMany(s => s.Records).Where(r => r.EventId.Id is >= 19000 and < 20000)];
        schedulerRecords.ShouldFollowLogConvention();
        schedulerRecords.ShouldOnlyLogEventIdsInSubBlock(LoggingEventIdRanges.Scheduling, 0);
        foreach ((int id, LogLevel level, string template) in ExpectedLogs)
        {
            schedulerRecords.ShouldHaveLogged(new EventId(id), level);
            schedulerRecords.ShouldHaveLoggedTemplate(new EventId(id), template);
        }

        ExpectedLogs.Select(e => e.Id).Should().BeEquivalentTo(DeclaredEventIds(), "every declared EventId has a pinned template and level");
    }

    [Fact]
    public async Task Success_OccurrenceLogsShareOneCorrelationIdAndTheJobsIdentityAndTenant()
    {
        TenantId tenant = new(Guid.NewGuid());
        ScenarioResult scenario = await SuccessAsync(tenant);

        AmbientLogRecord[] trio = [.. scenario.Ambient.Where(r => r.EventId.Id is 19003 or 19012 or 19004)];
        trio.Select(r => r.EventId.Id).Should().BeEquivalentTo([19003, 19012, 19004]);
        trio.Should().OnlyContain(r => r.Identity == scenario.JobName);
        trio.Should().OnlyContain(r => r.TenantId == tenant);
        trio.Select(r => r.CorrelationId).Distinct().Should().ContainSingle().Which.Should().NotBeNullOrWhiteSpace();

        AmbientLogRecord started = scenario.Ambient.First(r => r.EventId.Id == 19000);
        started.Identity.Should().Be(LoopIdentity);
        started.CorrelationId.Should().NotBe(trio[0].CorrelationId, "an occurrence gets its own correlation id");
        scenario.Ambient.Where(r => r.EventId.Id is 19000 or 19001)
            .Select(r => r.CorrelationId).Distinct().Should().ContainSingle("start and stop share the service instance's loop correlation id");
    }

    [Fact]
    public async Task Thrown_LogIsEmittedUnderTheJobsCaller_NotAfterTheScopeWasDisposed()
    {
        ScenarioResult scenario = await ThrownAsync("marker-thrown");

        AmbientLogRecord threw = scenario.Ambient.Single(r => r.EventId.Id == 19006);
        threw.HasContext.Should().BeTrue();
        threw.Identity.Should().Be(scenario.JobName);
        threw.CorrelationId.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Cancelled_LogIsEmittedUnderTheJobsCaller()
    {
        ScenarioResult scenario = await CancelledAsync();

        AmbientLogRecord cancelled = scenario.Ambient.Single(r => r.EventId.Id == 19014);
        cancelled.Identity.Should().Be(scenario.JobName);
        scenario.Ambient.Single(r => r.EventId.Id == 19001).Identity.Should().Be(LoopIdentity);
    }

    // ---- Spans ----------------------------------------------------------------------------------------------

    [Fact]
    public async Task Span_FailedResult_RecordsTheErrorCode_NeverTheMessage()
    {
        ScenarioResult scenario = await FailedResultAsync("marker-failed-result");

        Activity span = scenario.Span;
        span.ShouldBeRecordedFailure(scenario.FailedError!);
        span.ShouldNotLeak("marker-failed-result");
    }

    [Fact]
    public async Task Span_ThrownException_RecordsTheExceptionType_NeverTheMessageInStatusOrTags()
    {
        ScenarioResult scenario = await ThrownAsync("marker-thrown");

        scenario.Span.ShouldBeRecordedException(typeof(InvalidOperationException));
        scenario.Span.ShouldNotLeak("marker-thrown");
    }

    [Fact]
    public async Task Span_LockOutage_RecordsTheExceptionWithItsErrorCode()
    {
        ScenarioResult scenario = await LockOutageAsync();

        scenario.Span.ShouldBeRecordedException(typeof(DistributedLockUnavailableException), LockUnavailableCode);
    }

    [Fact]
    public async Task Span_LostClaim_LeavesTheStatusUnset()
    {
        ScenarioResult scenario = await LostClaimAsync();

        scenario.Span.ShouldBeUnset();
    }

    [Fact]
    public async Task Span_Success_LeavesTheStatusUnset()
    {
        ScenarioResult scenario = await SuccessAsync(tenant: null);

        scenario.Span.ShouldBeUnset();
    }

    [Fact]
    public async Task Span_ShutdownCancellation_IsNotRecordedAsAnError()
    {
        ScenarioResult scenario = await CancelledAsync();

        scenario.Span.ShouldBeRecordedCancellation();
    }

    // ---- Log text -------------------------------------------------------------------------------------------

    [Fact]
    public async Task Log_FailedResult_CarriesTheCodeOnly()
    {
        ScenarioResult scenario = await FailedResultAsync("marker-failed-result");

        scenario.Records.ShouldNotLeak("marker-failed-result");
        LogRecord record = scenario.Records.ShouldHaveLogged(new EventId(19005));
        record.TryGetProperty("ErrorCode", out object? code).Should().BeTrue();
        code.Should().Be(scenario.FailedError!.Code);
        record.TryGetProperty("ErrorMessage", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Log_IdempotencyOutageResult_LogsTheUnavailableCodeOnly()
    {
        Error outage = Error.Unavailable("idempotency.unavailable", "marker-idempotency-host db01.internal:6379");
        ScenarioResult scenario = await FailedResultAsync("unused", outage);

        scenario.Records.ShouldNotLeak("marker-idempotency-host", "db01.internal");
        LogRecord record = scenario.Records.ShouldHaveLogged(new EventId(19005));
        record.TryGetProperty("ErrorCode", out object? code).Should().BeTrue();
        code.Should().Be("idempotency.unavailable");
    }

    [Fact]
    public async Task Log_ThrownException_KeepsTheExceptionObject_ButNotItsMessageInTheText()
    {
        ScenarioResult scenario = await ThrownAsync("marker-thrown");

        scenario.Records.Should().OnlyContain(r => !r.Message.Contains("marker-thrown", StringComparison.Ordinal) && !(r.Template ?? string.Empty).Contains("marker-thrown", StringComparison.Ordinal) && (r.State == null || r.State.All(p => !(p.Value is string && ((string)p.Value).Contains("marker-thrown", StringComparison.Ordinal)))));
        scenario.Records.ShouldHaveLoggedWithException(new EventId(19006), typeof(InvalidOperationException));
    }

    [Fact]
    public async Task Log_Cancellation_CarriesNoException()
    {
        ScenarioResult scenario = await CancelledAsync();

        scenario.Records.ShouldHaveLoggedWithoutException(new EventId(19014));
    }

    [Fact]
    public async Task Log_DrainTimeout_CarriesNoException()
    {
        ScenarioResult scenario = await DrainTimeoutAsync();

        scenario.Records.ShouldHaveLoggedWithoutException(new EventId(19015));
    }

    [Fact]
    public async Task Log_DrainFault_KeepsTheException_ButNotItsMessageInTheText()
    {
        ScenarioResult scenario = await DrainFaultedAsync();

        scenario.Records.ShouldHaveLoggedWithException(new EventId(19017), typeof(InvalidOperationException));
        scenario.Records.Where(r => r.EventId.Id is >= 19000 and < 20000).ToList().ShouldNotLeak("marker-drain-fault");
    }

    [Fact]
    public async Task Categories_AreTheKernelTypes_NeverAConsumerType()
    {
        await using SchedulingTestHarness harness = Build(UniqueName("categories"));
        await harness.StartAsync();
        harness.Clock.Set(FirstDue);
        (await Eventually.UntilAsync(() => harness.Recorder.InvocationCount == 1, Wait)).Should().BeTrue();
        await harness.StopAsync();

        harness.LoggerFactory
            .ShouldHaveCategory(typeof(SharedKernel.Scheduling.Hosting.SchedulingHostedService))
            .ShouldHaveCategory(typeof(SharedKernel.Scheduling.Jobs.ScheduledCommandJob<>))
            .ShouldOnlyHaveCategoriesStartingWith("SharedKernel.");
        harness.LoggerFactory.Loggers.Keys.Should().Contain("SharedKernel.Scheduling.Jobs.ScheduledCommandJob");
    }

    [Fact]
    public async Task Log_LockOutage_KeepsTheException()
    {
        ScenarioResult scenario = await LockOutageAsync();

        scenario.Records.ShouldHaveLoggedWithException(new EventId(19016), typeof(DistributedLockUnavailableException));
    }

    // ---- No ambient leak ------------------------------------------------------------------------------------

    [Fact]
    public async Task NoAmbientContext_SurvivesStartFireOrStop_OnTheCallersThread()
    {
        RequestContextScope.Current.Should().BeNull();

        await using SchedulingTestHarness harness = SchedulingTestHarness.Build(
            registerJobs: builder => builder.AddRecurring<RecordingCommand>(
                UniqueName("no-leak"),
                "* * * * * ?",
                _ => new RecordingCommand(),
                options =>
                {
                    options.MisfirePolicy = MisfirePolicy.Skip;
                    options.OverlapPolicy = OverlapPolicy.Skip;
                }),
            initialClock: Start);

        Task start = harness.HostedService.StartAsync(CancellationToken.None);
        RequestContextScope.Current.Should().BeNull("the synchronous prefix of ExecuteAsync runs on the caller of StartAsync");
        await start;
        RequestContextScope.Current.Should().BeNull();

        (await Eventually.UntilAsync(() => harness.HostedService.IsRunning, Wait)).Should().BeTrue();
        RequestContextScope.Current.Should().BeNull();

        harness.Clock.Set(FirstDue);
        (await Eventually.UntilAsync(() => harness.Recorder.InvocationCount == 1, Wait)).Should().BeTrue();
        (await Eventually.UntilAsync(
            () => harness.LoggerFactory.GetLogger(typeof(SharedKernel.Scheduling.Hosting.SchedulingHostedService).FullName!).Records.Any(r => r.EventId.Id == 19003),
            Wait)).Should().BeTrue();
        RequestContextScope.Current.Should().BeNull();

        await harness.StopAsync();
        RequestContextScope.Current.Should().BeNull();
    }

    // ---- Scenarios ------------------------------------------------------------------------------------------

    private sealed record ScenarioResult(
        string JobName,
        IReadOnlyList<AmbientLogRecord> Ambient,
        IReadOnlyList<LogRecord> Records,
        IReadOnlyList<Activity> Spans,
        Error? FailedError = null)
    {
        public Activity Span => Spans.Single();
    }

    private static string UniqueName(string prefix) => $"{prefix}-{Guid.NewGuid():N}";

    private static SchedulingTestHarness Build(
        string jobName,
        IDistributedLockService? lockService = null,
        TenantScope tenantScope = default,
        MisfirePolicy misfire = MisfirePolicy.Skip,
        OverlapPolicy overlap = OverlapPolicy.Skip) =>
        SchedulingTestHarness.Build(
            registerJobs: builder => builder.AddRecurring<RecordingCommand>(
                jobName,
                "* * * * * ?",
                _ => new RecordingCommand(),
                options =>
                {
                    options.MisfirePolicy = misfire;
                    options.OverlapPolicy = overlap;
                    options.TenantScope = tenantScope;
                }),
            initialClock: Start,
            lockService: lockService);

    private static bool Has(SchedulingTestHarness harness, int eventId) =>
        harness.AmbientLoggerFactory.AllRecords.Any(r => r.EventId.Id == eventId);

    private static async Task<ScenarioResult> CollectAsync(string jobName, SchedulingTestHarness harness, ActivityRecorder recorder, Error? error = null)
    {
        IReadOnlyList<Activity> spans = [.. recorder.RecordedActivities
            .Where(a => string.Equals(a.GetTagItem(SchedulingTagKeys.JobName) as string, jobName, StringComparison.Ordinal))];
        ScenarioResult result = new(jobName, [.. harness.AmbientLoggerFactory.Ambient.Where(r => r.EventId.Id is >= 19000 and < 20000)], harness.AmbientLoggerFactory.AllRecords, spans, error);
        await harness.DisposeAsync();
        recorder.Dispose();
        return result;
    }

    private static async Task WaitForSpanAsync(ActivityRecorder recorder, string jobName)
    {
        (await Eventually.UntilAsync(
            () => recorder.RecordedActivities.Any(a => string.Equals(a.GetTagItem(SchedulingTagKeys.JobName) as string, jobName, StringComparison.Ordinal)),
            Wait)).Should().BeTrue("the occurrence span must have been recorded");
    }

    private static async Task<ScenarioResult> SuccessAsync(TenantId? tenant)
    {
        string jobName = UniqueName("success");
        using ActivityRecorder recorder = ActivityRecorder.StartRecording(SchedulingTelemetry.Name);
        SchedulingTestHarness harness = Build(
            jobName,
            new FakeDistributedLockService(),
            tenant is { } t ? TenantScope.For(t) : TenantScope.Global);

        await harness.StartAsync();
        harness.Clock.Set(FirstDue);
        (await Eventually.UntilAsync(() => Has(harness, 19004), Wait)).Should().BeTrue();
        await WaitForSpanAsync(recorder, jobName);
        await harness.StopAsync();
        return await CollectAsync(jobName, harness, recorder);
    }

    private static async Task<ScenarioResult> FailedResultAsync(string marker, Error? error = null)
    {
        string jobName = UniqueName("failed");
        Error failure = error ?? Error.Conflict("scheduling.test.conflict", marker);
        using ActivityRecorder recorder = ActivityRecorder.StartRecording(SchedulingTelemetry.Name);
        SchedulingTestHarness harness = Build(jobName);
        harness.Recorder.ResultFactory = () => Result.Failure(failure);

        await harness.StartAsync();
        harness.Clock.Set(FirstDue);
        (await Eventually.UntilAsync(() => Has(harness, 19005), Wait)).Should().BeTrue();
        await WaitForSpanAsync(recorder, jobName);
        await harness.StopAsync();
        return await CollectAsync(jobName, harness, recorder, failure);
    }

    private static async Task<ScenarioResult> ThrownAsync(string marker)
    {
        string jobName = UniqueName("thrown");
        using ActivityRecorder recorder = ActivityRecorder.StartRecording(SchedulingTelemetry.Name);
        SchedulingTestHarness harness = Build(jobName);
        harness.Recorder.OnHandling = (_, _) => throw new InvalidOperationException(marker);

        await harness.StartAsync();
        harness.Clock.Set(FirstDue);
        (await Eventually.UntilAsync(() => Has(harness, 19006), Wait)).Should().BeTrue();
        await WaitForSpanAsync(recorder, jobName);
        await harness.StopAsync();
        return await CollectAsync(jobName, harness, recorder);
    }

    private static async Task<ScenarioResult> CancelledAsync()
    {
        string jobName = UniqueName("cancelled");
        using ActivityRecorder recorder = ActivityRecorder.StartRecording(SchedulingTelemetry.Name);
        SchedulingTestHarness harness = Build(jobName);
        harness.Recorder.OnHandling = (_, ct) => Task.Delay(Timeout.InfiniteTimeSpan, ct);

        await harness.StartAsync();
        harness.Clock.Set(FirstDue);
        (await Eventually.UntilAsync(() => harness.Recorder.InvocationCount == 1, Wait)).Should().BeTrue();
        await harness.StopAsync();
        (await Eventually.UntilAsync(() => Has(harness, 19014), Wait)).Should().BeTrue();
        await WaitForSpanAsync(recorder, jobName);
        return await CollectAsync(jobName, harness, recorder);
    }

    private static async Task<ScenarioResult> LostClaimAsync()
    {
        string jobName = UniqueName("lost-claim");
        var lockService = Substitute.For<IDistributedLockService>();
        lockService
            .TryAcquireLeaseAsync(Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<DistributedLease?>(null));
        using ActivityRecorder recorder = ActivityRecorder.StartRecording(SchedulingTelemetry.Name);
        SchedulingTestHarness harness = Build(jobName, lockService);

        await harness.StartAsync();
        harness.Clock.Set(FirstDue);
        (await Eventually.UntilAsync(() => Has(harness, 19011), Wait)).Should().BeTrue();
        await WaitForSpanAsync(recorder, jobName);
        await harness.StopAsync();
        return await CollectAsync(jobName, harness, recorder);
    }

    private static async Task<ScenarioResult> LockOutageAsync()
    {
        string jobName = UniqueName("outage");
        using ActivityRecorder recorder = ActivityRecorder.StartRecording(SchedulingTelemetry.Name);
        SchedulingTestHarness harness = Build(jobName, new FakeDistributedLockService { SimulateUnavailable = true });

        await harness.StartAsync();
        harness.Clock.Set(FirstDue);
        (await Eventually.UntilAsync(() => Has(harness, 19016), Wait)).Should().BeTrue();
        await WaitForSpanAsync(recorder, jobName);
        await harness.StopAsync();
        return await CollectAsync(jobName, harness, recorder);
    }

    private static async Task<ScenarioResult> MisfireAsync()
    {
        string jobName = UniqueName("misfire");
        SchedulingTestHarness harness = SchedulingTestHarness.Build(
            registerJobs: builder => builder.AddRecurring<RecordingCommand>(
                jobName,
                "0 * * * * ?",
                _ => new RecordingCommand(),
                options =>
                {
                    options.MisfirePolicy = MisfirePolicy.Skip;
                    options.OverlapPolicy = OverlapPolicy.Allow;
                }),
            initialClock: Start.AddSeconds(30));
        using ActivityRecorder recorder = ActivityRecorder.StartRecording(SchedulingTelemetry.Name);

        await harness.StartAsync();
        harness.Clock.Set(Start.AddMinutes(12));
        (await Eventually.UntilAsync(() => Has(harness, 19010), Wait)).Should().BeTrue();
        await harness.StopAsync();
        return await CollectAsync(jobName, harness, recorder);
    }

    private static async Task<ScenarioResult> OverlapAsync(OverlapPolicy policy)
    {
        string jobName = UniqueName("overlap");
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using ActivityRecorder recorder = ActivityRecorder.StartRecording(SchedulingTelemetry.Name);
        SchedulingTestHarness harness = Build(jobName, overlap: policy);
        harness.Recorder.OnHandling = async (index, ct) =>
        {
            if (index == 1)
            {
                await release.Task.WaitAsync(ct).ConfigureAwait(false);
            }
        };

        await harness.StartAsync();
        harness.Clock.Set(FirstDue);
        (await Eventually.UntilAsync(() => harness.Recorder.InvocationCount >= 1, Wait)).Should().BeTrue();
        harness.Clock.Set(SecondDue);
        int expected = policy == OverlapPolicy.Skip ? 19007 : 19008;
        (await Eventually.UntilAsync(() => Has(harness, expected), Wait)).Should().BeTrue();
        release.SetResult();
        await harness.StopAsync();
        return await CollectAsync(jobName, harness, recorder);
    }

    private static async Task<ScenarioResult> FaultedLoopAsync()
    {
        string jobName = UniqueName("faulted");
        using ActivityRecorder recorder = ActivityRecorder.StartRecording(SchedulingTelemetry.Name);
        // A tick interval below the validated floor makes IOptions<SchedulingOptions>.Value throw inside the loop.
        SchedulingTestHarness harness = SchedulingTestHarness.Build(
            registerJobs: builder => builder.AddRecurring<RecordingCommand>(
                jobName,
                "* * * * * ?",
                _ => new RecordingCommand(),
                options =>
                {
                    options.MisfirePolicy = MisfirePolicy.Skip;
                    options.OverlapPolicy = OverlapPolicy.Skip;
                }),
            configureOptions: o => o.TickInterval = TimeSpan.FromMilliseconds(1),
            initialClock: Start);

        await harness.HostedService.StartAsync(CancellationToken.None);
        (await Eventually.UntilAsync(() => Has(harness, 19013), Wait)).Should().BeTrue();
        try
        {
            await harness.StopAsync();
        }
        catch (Exception)
        {
            // The faulted loop task surfaces its fault on stop; the log under test was already written.
        }

        return await CollectAsync(jobName, harness, recorder);
    }

    private static async Task<ScenarioResult> DrainTimeoutAsync()
    {
        string jobName = UniqueName("drain");
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using ActivityRecorder recorder = ActivityRecorder.StartRecording(SchedulingTelemetry.Name);
        SchedulingTestHarness harness = Build(jobName);
        // Ignores the stopping token, so the drain has to time out.
        harness.Recorder.OnHandling = (_, _) => release.Task;

        await harness.StartAsync();
        harness.Clock.Set(FirstDue);
        (await Eventually.UntilAsync(() => harness.Recorder.InvocationCount == 1, Wait)).Should().BeTrue();

        using var drainBudget = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        await harness.StopAsync(drainBudget.Token);
        Has(harness, 19015).Should().BeTrue();

        release.SetResult();
        await Eventually.UntilAsync(() => Has(harness, 19004), TimeSpan.FromSeconds(2));
        return await CollectAsync(jobName, harness, recorder);
    }

    private static async Task<ScenarioResult> DrainFaultedAsync()
    {
        string jobName = UniqueName("drain-faulted");
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using ActivityRecorder recorder = ActivityRecorder.StartRecording(SchedulingTelemetry.Name);
        SchedulingTestHarness harness = Build(jobName);
        // The handler throws once released, and the logger then throws for that failure's own log record, so
        // the in-flight task faults after the drain has begun waiting for it.
        harness.Recorder.OnHandling = async (_, _) =>
        {
            await release.Task.ConfigureAwait(false);
            throw new InvalidOperationException("marker-drain-fault");
        };
        harness.AmbientLoggerFactory.ThrowWhen = id => id.Id == 19006;

        await harness.StartAsync();
        harness.Clock.Set(FirstDue);
        (await Eventually.UntilAsync(() => harness.Recorder.InvocationCount == 1, Wait)).Should().BeTrue();

        Task stop = harness.StopAsync();
        (await Eventually.UntilAsync(() => !harness.HostedService.IsRunning && Has(harness, 19001), Wait)).Should().BeTrue();
        await Task.Delay(TimeSpan.FromMilliseconds(500));
        release.SetResult();
        await stop.WaitAsync(Wait);

        Has(harness, 19017).Should().BeTrue();
        return await CollectAsync(jobName, harness, recorder);
    }

    private static HashSet<int> DeclaredEventIds() =>
    [
        .. typeof(SchedulingLog)
            .GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .Select(m => m.GetCustomAttribute<LoggerMessageAttribute>())
            .Where(a => a is not null)
            .Select(a => a!.EventId),
    ];
}

using System.Diagnostics;
using MsOptions = Microsoft.Extensions.Options.Options;
using SharedKernel.Execution.Context;
using SharedKernel.Idempotency.Abstractions;
using SharedKernel.Idempotency.Redis.Options;
using SharedKernel.Idempotency.Redis.Store;
using SharedKernel.Idempotency.Redis.Tests.Support;
using SharedKernel.Primitives.Propagation;
using SharedKernel.Testing.Containers;
using SharedKernel.Testing.Logging;
using SharedKernel.Testing.Telemetry;
using StackExchange.Redis;
using Xunit;

namespace SharedKernel.Idempotency.Redis.Tests.Concurrency;

/// <summary>
/// The spans and metrics of the Redis store against a real Redis container, read through the shared capture helpers.
/// The class shares the Redis collection, which runs its tests one at a time, so a recorder listening to the shared
/// <c>SharedKernel.Idempotency</c> names sees only the test that started it. Instrumentation must not change the
/// atomicity the concurrency tests prove.
/// </summary>
/// <remarks>REQUIRES A DOCKER DAEMON (Testcontainers Redis).</remarks>
[Collection("RedisContainer")]
public sealed class RedisIdempotencyTelemetryTests(RedisContainerFixture fixture)
{
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan Retention = TimeSpan.FromMinutes(5);

    private sealed class FixedTenantAccessor(Guid tenantId) : IRequestContextAccessor
    {
        public IRequestContext? Current { get; } =
            new SystemRequestContext([], "test", SharedKernel.Execution.Tenancy.TenantId.FromNullable(tenantId));
    }

    private static RedisIdempotencyStore CreateStore(IConnectionMultiplexer multiplexer, Guid tenantId) =>
        new(
            multiplexer,
            new FixedTenantAccessor(tenantId),
            MsOptions.Create(new RedisIdempotencyOptions()),
            new InMemoryLogger<RedisIdempotencyStore>(),
            TestMeterFactory.NewMetrics());

    private static KeyValuePair<string, object?> Tag(string key, object value) => new(key, value);

    [Fact]
    public async Task ConcurrentBegins_OnOneKey_ProduceOneStartedAndTheRestInProgress_AndOneSpanEach()
    {
        await using var multiplexer = await ConnectionMultiplexer.ConnectAsync(fixture.ConnectionString);
        using var spans = ActivityRecorder.StartRecording(IdempotencyTelemetry.SourceName);
        using var metrics = MetricRecorder.StartRecording(IdempotencyTelemetry.MeterName);
        var store = CreateStore(multiplexer, Guid.NewGuid());
        var key = $"telemetry-{Guid.NewGuid():N}";
        const int concurrency = 32;

        // An async start gate, not a blocking Barrier: parked threads would starve the thread pool the Redis
        // completions need.
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = Enumerable.Range(0, concurrency).Select(_ => Task.Run(async () =>
        {
            await start.Task;
            return await store.TryBeginAsync(IdempotencyPurpose.Request, key, "fp", Ttl, CancellationToken.None);
        })).ToArray();
        start.SetResult();
        var results = await Task.WhenAll(tasks);

        Assert.Single(results, r => r.Status == IdempotencyReservationStatus.Started);
        Assert.Equal(1, metrics.Count(IdempotencyTelemetry.OperationDurationInstrument, Tag(IdempotencyTagKeys.Result, "started")));
        Assert.Equal(
            concurrency - 1,
            metrics.Count(IdempotencyTelemetry.OperationDurationInstrument, Tag(IdempotencyTagKeys.Result, "in_progress")));
        Assert.Equal(concurrency, metrics.Count(IdempotencyTelemetry.OperationDurationInstrument));
        spans.RecordedActivities.ShouldHaveSpanCount("idempotency begin", concurrency);
        Assert.Equal(1, spans.RecordedActivities.Count(a => a.GetTagItem(IdempotencyTagKeys.Result) as string == "started"));
        Assert.Equal(0, metrics.Count(IdempotencyTelemetry.FailOpenInstrument));
    }

    [Fact]
    public async Task EveryBeginResult_IsMeasuredAndTaggedOnTheSpan()
    {
        await using var multiplexer = await ConnectionMultiplexer.ConnectAsync(fixture.ConnectionString);
        using var spans = ActivityRecorder.StartRecording(IdempotencyTelemetry.SourceName);
        using var metrics = MetricRecorder.StartRecording(IdempotencyTelemetry.MeterName);
        var store = CreateStore(multiplexer, Guid.NewGuid());
        var key = $"telemetry-{Guid.NewGuid():N}";

        var started = await store.TryBeginAsync(IdempotencyPurpose.Message, key, "fp", Ttl, CancellationToken.None);
        await store.TryBeginAsync(IdempotencyPurpose.Message, key, "fp", Ttl, CancellationToken.None);
        await store.TryBeginAsync(IdempotencyPurpose.Message, key, "other-fp", Ttl, CancellationToken.None);
        await store.CompleteAsync(IdempotencyPurpose.Message, key, started.Token!, null, Retention, CancellationToken.None);
        await store.TryBeginAsync(IdempotencyPurpose.Message, key, "fp", Ttl, CancellationToken.None);

        foreach (var result in new[] { "started", "in_progress", "fingerprint_mismatch", "completed" })
        {
            metrics.ShouldHaveMeasurement(
                IdempotencyTelemetry.OperationDurationInstrument,
                value: null,
                Tag(IdempotencyTagKeys.Result, result),
                Tag(IdempotencyTagKeys.Operation, "begin"),
                Tag(IdempotencyTagKeys.Purpose, "message"),
                Tag(IdempotencyTagKeys.Provider, "redis"),
                Tag(WellKnownTagKeys.Outcome, WellKnownOutcomes.Success));
        }

        var beginSpans = spans.RecordedActivities.Where(a => a.OperationName == "idempotency begin").ToList();
        Assert.Equal(
            ["started", "in_progress", "fingerprint_mismatch", "completed"],
            beginSpans.Select(a => (string)a.GetTagItem(IdempotencyTagKeys.Result)!).ToArray());
        foreach (var span in beginSpans)
        {
            span.ShouldHaveKind(ActivityKind.Client)
                .ShouldHaveTag(IdempotencyTagKeys.Provider, "redis")
                .ShouldHaveTag(IdempotencyTagKeys.Purpose, "message")
                .ShouldHaveTag(IdempotencyTagKeys.Operation, "begin")
                .ShouldBeRecordedSuccess();
        }
    }

    [Fact]
    public async Task CompleteAndRelease_ReportAppliedForTheOwner_AndLostForStaleForeignAndCompletedTokens()
    {
        await using var multiplexer = await ConnectionMultiplexer.ConnectAsync(fixture.ConnectionString);
        using var spans = ActivityRecorder.StartRecording(IdempotencyTelemetry.SourceName);
        using var metrics = MetricRecorder.StartRecording(IdempotencyTelemetry.MeterName);
        var store = CreateStore(multiplexer, Guid.NewGuid());
        var key = $"telemetry-{Guid.NewGuid():N}";
        var foreign = Guid.NewGuid().ToString();

        var first = await store.TryBeginAsync(IdempotencyPurpose.Request, key, "fp", Ttl, CancellationToken.None);
        Assert.False(await store.CompleteAsync(IdempotencyPurpose.Request, key, foreign, "r", Retention, CancellationToken.None));
        Assert.False(await store.ReleaseAsync(IdempotencyPurpose.Request, key, foreign, CancellationToken.None));
        Assert.True(await store.CompleteAsync(IdempotencyPurpose.Request, key, first.Token!, "r", Retention, CancellationToken.None));
        Assert.False(await store.ReleaseAsync(IdempotencyPurpose.Request, key, first.Token!, CancellationToken.None));
        Assert.False(await store.CompleteAsync(IdempotencyPurpose.Request, key, first.Token!, "r", Retention, CancellationToken.None));

        var second = await store.TryBeginAsync(IdempotencyPurpose.Request, $"{key}-b", "fp", Ttl, CancellationToken.None);
        Assert.True(await store.ReleaseAsync(IdempotencyPurpose.Request, $"{key}-b", second.Token!, CancellationToken.None));

        Assert.Equal(
            1,
            metrics.Count(IdempotencyTelemetry.OperationDurationInstrument, Tag(IdempotencyTagKeys.Operation, "complete"), Tag(IdempotencyTagKeys.Result, "applied")));
        Assert.Equal(
            2,
            metrics.Count(IdempotencyTelemetry.OperationDurationInstrument, Tag(IdempotencyTagKeys.Operation, "complete"), Tag(IdempotencyTagKeys.Result, "lost")));
        Assert.Equal(
            1,
            metrics.Count(IdempotencyTelemetry.OperationDurationInstrument, Tag(IdempotencyTagKeys.Operation, "release"), Tag(IdempotencyTagKeys.Result, "applied")));
        Assert.Equal(
            2,
            metrics.Count(IdempotencyTelemetry.OperationDurationInstrument, Tag(IdempotencyTagKeys.Operation, "release"), Tag(IdempotencyTagKeys.Result, "lost")));
        spans.RecordedActivities.ShouldHaveSpanCount("idempotency complete", 3);
        spans.RecordedActivities.ShouldHaveSpanCount("idempotency release", 3);
        Assert.All(
            spans.RecordedActivities.Where(a => a.OperationName != "idempotency begin"),
            span => Assert.Contains(span.GetTagItem(IdempotencyTagKeys.Result) as string, new[] { "applied", "lost" }));
    }

    [Fact]
    public async Task Instruments_HaveUnits_AndTheRecordedTelemetryFollowsTheConventions()
    {
        await using var multiplexer = await ConnectionMultiplexer.ConnectAsync(fixture.ConnectionString);
        using var spans = ActivityRecorder.StartRecording(IdempotencyTelemetry.SourceName);
        using var metrics = MetricRecorder.StartRecording(IdempotencyTelemetry.MeterName);
        var store = CreateStore(multiplexer, Guid.NewGuid());
        var key = $"telemetry-{Guid.NewGuid():N}";

        var begin = await store.TryBeginAsync(IdempotencyPurpose.Request, key, "fp", Ttl, CancellationToken.None);
        await store.CompleteAsync(IdempotencyPurpose.Request, key, begin.Token!, "r", Retention, CancellationToken.None);
        await store.ReleaseAsync(IdempotencyPurpose.Request, key, begin.Token!, CancellationToken.None);

        metrics
            .ShouldHaveInstrument(IdempotencyTelemetry.OperationDurationInstrument, "s", InstrumentKind.Histogram)
            .ShouldHaveInstrument(IdempotencyTelemetry.FailOpenInstrument, "{operation}", InstrumentKind.Counter)
            .ShouldUseOutcomeVocabulary(IdempotencyTelemetry.OperationDurationInstrument)
            .ShouldHaveTagSet(
                IdempotencyTelemetry.OperationDurationInstrument,
                IdempotencyTagKeys.Operation,
                IdempotencyTagKeys.Purpose,
                IdempotencyTagKeys.Provider,
                WellKnownTagKeys.Outcome,
                IdempotencyTagKeys.Result)
            .ShouldFollowMetricConvention();
        spans.RecordedActivities.ShouldFollowSpanConvention();
        Assert.All(spans.RecordedActivities, span => Assert.Equal("SharedKernel.Idempotency", span.Source.Name));
    }

    [Fact]
    public async Task RealTraffic_NeverPutsTheKeyFingerprintTokenTenantOrResponseOnASpanOrMetric()
    {
        await using var multiplexer = await ConnectionMultiplexer.ConnectAsync(fixture.ConnectionString);
        using var spans = ActivityRecorder.StartRecording(IdempotencyTelemetry.SourceName);
        using var metrics = MetricRecorder.StartRecording(IdempotencyTelemetry.MeterName);
        var tenant = Guid.NewGuid();
        var store = CreateStore(multiplexer, tenant);
        var key = $"sentinel-key-{Guid.NewGuid():N}";
        const string fingerprint = "sentinel-fingerprint-3b9d";
        const string response = "sentinel-response-77e0";

        var begin = await store.TryBeginAsync(IdempotencyPurpose.Request, key, fingerprint, Ttl, CancellationToken.None);
        await store.TryBeginAsync(IdempotencyPurpose.Request, key, "other", Ttl, CancellationToken.None);
        await store.CompleteAsync(IdempotencyPurpose.Request, key, begin.Token!, response, Retention, CancellationToken.None);
        await store.TryBeginAsync(IdempotencyPurpose.Request, key, fingerprint, Ttl, CancellationToken.None);
        await store.ReleaseAsync(IdempotencyPurpose.Request, key, begin.Token!, CancellationToken.None);

        string[] sentinels = [key, fingerprint, begin.Token!, response, tenant.ToString()];
        spans.RecordedActivities.ShouldNotLeak(sentinels);
        metrics.ShouldNotLeak(sentinels);
    }
}

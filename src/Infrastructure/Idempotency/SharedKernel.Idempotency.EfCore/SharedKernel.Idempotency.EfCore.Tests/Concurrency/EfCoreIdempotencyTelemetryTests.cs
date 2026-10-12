using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using MsOptions = Microsoft.Extensions.Options.Options;
using SharedKernel.Execution.Context;
using SharedKernel.Idempotency.Abstractions;
using SharedKernel.Idempotency.EfCore.Context;
using SharedKernel.Idempotency.EfCore.Options;
using SharedKernel.Idempotency.EfCore.Store;
using SharedKernel.Idempotency.EfCore.Tests.Support;
using SharedKernel.Persistence;
using SharedKernel.Primitives.Propagation;
using SharedKernel.Testing.Clocks;
using SharedKernel.Testing.Containers;
using SharedKernel.Testing.Logging;
using SharedKernel.Testing.Persistence;
using SharedKernel.Testing.Telemetry;
using Xunit;

namespace SharedKernel.Idempotency.EfCore.Tests.Concurrency;

/// <summary>
/// The spans and metrics of the EF Core store against a real PostgreSQL container, read through the shared capture
/// helpers. The class shares the PostgreSQL collection, which runs its tests one at a time, so a recorder listening to
/// the shared <c>SharedKernel.Idempotency</c> names sees only the test that started it. Instrumentation must not change
/// the atomicity the concurrency tests prove.
/// </summary>
/// <remarks>REQUIRES A DOCKER DAEMON (Testcontainers PostgreSQL).</remarks>
[Collection("PostgreSqlContainer")]
public sealed class EfCoreIdempotencyTelemetryTests : IAsyncLifetime
{
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan Retention = TimeSpan.FromMinutes(5);

    private readonly PostgreSqlContainerFixture _fixture;

    public EfCoreIdempotencyTelemetryTests(PostgreSqlContainerFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        await using var context = CreateContext();
        await context.Database.EnsureCreatedAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private sealed class FixedTenantAccessor(Guid tenantId) : IRequestContextAccessor
    {
        public IRequestContext? Current { get; } =
            new SystemRequestContext([], "test", SharedKernel.Execution.Tenancy.TenantId.FromNullable(tenantId));
    }

    private IdempotencyDbContext CreateContext()
    {
        var optionsBuilder = new DbContextOptionsBuilder<IdempotencyDbContext>();
        optionsBuilder.UsePostgres(TestNpgsqlDataSources.Get(_fixture.ConnectionString));
        return new IdempotencyDbContext(optionsBuilder.Options);
    }

    private EfCoreIdempotencyStore CreateStore(Guid tenantId, FakeClock clock) =>
        new(
            CreateContext(),
            new FixedTenantAccessor(tenantId),
            clock,
            MsOptions.Create(new EfCoreIdempotencyOptions()),
            new InMemoryLogger<EfCoreIdempotencyStore>(),
            TestMeterFactory.NewMetrics());

    [Fact]
    public async Task ConcurrentBegins_OnOneKey_ProduceOneStartedAndTheRestInProgress_AndOneSpanEach()
    {
        using var spans = ActivityRecorder.StartRecording(IdempotencyTelemetry.SourceName);
        using var metrics = MetricRecorder.StartRecording(IdempotencyTelemetry.MeterName);
        var tenant = Guid.NewGuid();
        var key = $"telemetry-{Guid.NewGuid():N}";
        const int concurrency = 32;

        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = Enumerable.Range(0, concurrency).Select(_ => Task.Run(async () =>
        {
            var store = CreateStore(tenant, new FakeClock());
            await start.Task;
            return await store.TryBeginAsync(IdempotencyPurpose.Request, key, "fp", Ttl, CancellationToken.None);
        })).ToArray();
        start.SetResult();
        var results = await Task.WhenAll(tasks);

        Assert.Single(results, r => r.Status == IdempotencyReservationStatus.Started);
        Assert.Equal(1, metrics.Count(IdempotencyTelemetry.OperationDurationInstrument, Tags.Of(IdempotencyTagKeys.Result, "started")));
        Assert.Equal(
            concurrency - 1,
            metrics.Count(IdempotencyTelemetry.OperationDurationInstrument, Tags.Of(IdempotencyTagKeys.Result, "in_progress")));
        Assert.Equal(concurrency, metrics.Count(IdempotencyTelemetry.OperationDurationInstrument));
        spans.RecordedActivities.ShouldHaveSpanCount("idempotency begin", concurrency);
        Assert.Equal(1, spans.RecordedActivities.Count(a => a.GetTagItem(IdempotencyTagKeys.Result) as string == "started"));
        Assert.Equal(0, metrics.Count(IdempotencyTelemetry.FailOpenInstrument));
    }

    [Fact]
    public async Task EveryBeginResult_IsMeasuredAndTaggedOnTheSpan_IncludingAnExpiredRowReclaimedAsStarted()
    {
        using var spans = ActivityRecorder.StartRecording(IdempotencyTelemetry.SourceName);
        using var metrics = MetricRecorder.StartRecording(IdempotencyTelemetry.MeterName);
        var tenant = Guid.NewGuid();
        var clock = new FakeClock();
        var store = CreateStore(tenant, clock);
        var key = $"telemetry-{Guid.NewGuid():N}";

        var started = await store.TryBeginAsync(IdempotencyPurpose.Message, key, "fp", Ttl, CancellationToken.None);
        await store.TryBeginAsync(IdempotencyPurpose.Message, key, "fp", Ttl, CancellationToken.None);
        await store.TryBeginAsync(IdempotencyPurpose.Message, key, "other-fp", Ttl, CancellationToken.None);
        await store.CompleteAsync(IdempotencyPurpose.Message, key, started.Token!, null, Retention, CancellationToken.None);
        await store.TryBeginAsync(IdempotencyPurpose.Message, key, "fp", Ttl, CancellationToken.None);

        clock.Advance(Retention + TimeSpan.FromSeconds(1));
        var reclaimed = await store.TryBeginAsync(IdempotencyPurpose.Message, key, "fp", Ttl, CancellationToken.None);
        Assert.Equal(IdempotencyReservationStatus.Started, reclaimed.Status);

        foreach (var result in new[] { "started", "in_progress", "fingerprint_mismatch", "completed" })
        {
            metrics.ShouldHaveMeasurement(
                IdempotencyTelemetry.OperationDurationInstrument,
                value: null,
                Tags.Of(IdempotencyTagKeys.Result, result),
                Tags.Of(IdempotencyTagKeys.Operation, "begin"),
                Tags.Of(IdempotencyTagKeys.Purpose, "message"),
                Tags.Of(IdempotencyTagKeys.Provider, "efcore"),
                Tags.Of(WellKnownTagKeys.Outcome, WellKnownOutcomes.Success));
        }

        // The reclaim of the expired row is counted as a second "started".
        Assert.Equal(2, metrics.Count(IdempotencyTelemetry.OperationDurationInstrument, Tags.Of(IdempotencyTagKeys.Result, "started")));
        var beginSpans = spans.RecordedActivities.Where(a => a.OperationName == "idempotency begin").ToList();
        Assert.Equal(
            ["started", "in_progress", "fingerprint_mismatch", "completed", "started"],
            beginSpans.Select(a => (string)a.GetTagItem(IdempotencyTagKeys.Result)!).ToArray());
        foreach (var span in beginSpans)
        {
            span.ShouldHaveKind(ActivityKind.Client)
                .ShouldHaveTag(IdempotencyTagKeys.Provider, "efcore")
                .ShouldHaveTag(IdempotencyTagKeys.Purpose, "message")
                .ShouldHaveTag(IdempotencyTagKeys.Operation, "begin")
                .ShouldBeRecordedSuccess();
        }
    }

    [Fact]
    public async Task CompleteAndRelease_ReportAppliedForTheOwner_AndLostForStaleForeignAndCompletedTokens()
    {
        using var spans = ActivityRecorder.StartRecording(IdempotencyTelemetry.SourceName);
        using var metrics = MetricRecorder.StartRecording(IdempotencyTelemetry.MeterName);
        var store = CreateStore(Guid.NewGuid(), new FakeClock());
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
            metrics.Count(IdempotencyTelemetry.OperationDurationInstrument, Tags.Of(IdempotencyTagKeys.Operation, "complete"), Tags.Of(IdempotencyTagKeys.Result, "applied")));
        Assert.Equal(
            2,
            metrics.Count(IdempotencyTelemetry.OperationDurationInstrument, Tags.Of(IdempotencyTagKeys.Operation, "complete"), Tags.Of(IdempotencyTagKeys.Result, "lost")));
        Assert.Equal(
            1,
            metrics.Count(IdempotencyTelemetry.OperationDurationInstrument, Tags.Of(IdempotencyTagKeys.Operation, "release"), Tags.Of(IdempotencyTagKeys.Result, "applied")));
        Assert.Equal(
            2,
            metrics.Count(IdempotencyTelemetry.OperationDurationInstrument, Tags.Of(IdempotencyTagKeys.Operation, "release"), Tags.Of(IdempotencyTagKeys.Result, "lost")));
        spans.RecordedActivities.ShouldHaveSpanCount("idempotency complete", 3);
        spans.RecordedActivities.ShouldHaveSpanCount("idempotency release", 3);
    }

    [Fact]
    public async Task Instruments_HaveUnits_AndTheRecordedTelemetryFollowsTheConventions()
    {
        using var spans = ActivityRecorder.StartRecording(IdempotencyTelemetry.SourceName);
        using var metrics = MetricRecorder.StartRecording(IdempotencyTelemetry.MeterName);
        var store = CreateStore(Guid.NewGuid(), new FakeClock());
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
    }

    [Fact]
    public async Task RealTraffic_NeverPutsTheKeyFingerprintTokenTenantOrResponseOnASpanOrMetric()
    {
        using var spans = ActivityRecorder.StartRecording(IdempotencyTelemetry.SourceName);
        using var metrics = MetricRecorder.StartRecording(IdempotencyTelemetry.MeterName);
        var tenant = Guid.NewGuid();
        var store = CreateStore(tenant, new FakeClock());
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

using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Caching.FusionCache.Diagnostics;
using SharedKernel.Caching.FusionCache.Extensions;
using SharedKernel.Caching.FusionCache.Tests.Telemetry;
using SharedKernel.Primitives.Propagation;
using SharedKernel.Testing.Telemetry;
using Xunit;

namespace SharedKernel.Caching.FusionCache.Tests;

/// <summary>
/// The cache's instruments, recorded through the shared <see cref="MetricRecorder"/>. The meter is created through
/// <c>IMeterFactory</c> per service provider, and the assembly runs serially (see <c>AssemblyInfo</c>), so each test
/// sees the measurements of its own provider; every test also uses its own entity so its tag series is unique.
/// </summary>
public sealed class OtelMetricsTests : IDisposable
{
    private readonly ServiceProvider _provider;
    private readonly ICacheService _cache;
    private readonly MetricRecorder _metrics = MetricRecorder.StartRecording(CachingTelemetry.MeterName);
    private readonly string _entity = "e" + Guid.NewGuid().ToString("N");

    public OtelMetricsTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSharedKernelCaching(o => o.ServiceName = "svc");
        _provider = services.BuildServiceProvider();
        _cache = _provider.GetRequiredService<ICacheService>();
    }

    public void Dispose()
    {
        _metrics.Dispose();
        _provider.Dispose();
    }

    private string Prefix => "svc:" + _entity;

    private string Key(string id) => CacheKeyFormat.BuildKey("svc", _entity, id);

    private static KeyValuePair<string, object?> Tag(string key, object? value) => new(key, value);

    [Fact]
    public async Task Hits_AreCounted_WithTheKeyPrefixAndTheMemoryLevel()
    {
        await _cache.SetAsync(Key("1"), "value", CachePolicy.Default);

        var lookup = await _cache.TryGetAsync<string>(Key("1"));

        Assert.Equal("value", lookup.Value);
        Assert.True(await Eventually.HoldsAsync(() => _metrics.Sum(
            CachingTelemetry.HitsInstrument,
            Tag(CachingTagKeys.KeyPrefix, Prefix),
            Tag(CachingTagKeys.Level, CachingTagValues.LevelMemory)) >= 1));
        _metrics.ShouldHaveTagSet(CachingTelemetry.HitsInstrument, CachingTagKeys.KeyPrefix, CachingTagKeys.Level);
    }

    [Fact]
    public async Task Misses_AreCounted_OnATryGetMiss()
    {
        var lookup = await _cache.TryGetAsync<string>(Key("absent"));

        Assert.False(lookup.IsHit);
        _metrics.ShouldHaveSum(CachingTelemetry.MissesInstrument, 1, Tag(CachingTagKeys.KeyPrefix, Prefix));
        _metrics.ShouldHaveTagSet(CachingTelemetry.MissesInstrument, CachingTagKeys.KeyPrefix);
    }

    [Fact]
    public async Task Misses_AreCounted_OnceAtTheFactoryRun()
    {
        var value = await _cache.GetOrSetAsync(Key("computed"), _ => ValueTask.FromResult("v"), CachePolicy.Default);

        Assert.Equal("v", value);
        _metrics.ShouldHaveSum(CachingTelemetry.MissesInstrument, 1, Tag(CachingTagKeys.KeyPrefix, Prefix));
    }

    [Fact]
    public async Task FactoryDuration_IsRecordedInSeconds_WithTheSuccessOutcome()
    {
        await _cache.GetOrSetAsync(
            Key("timed"),
            async ct =>
            {
                await Task.Delay(60, ct);
                return "timed";
            },
            CachePolicy.Default);

        var measurement = Assert.Single(
            _metrics.MeasurementsOf(CachingTelemetry.FactoryDurationInstrument),
            m => m.TryGetTag(CachingTagKeys.KeyPrefix, out var prefix) && (string?)prefix == Prefix);

        // 60 ms of delay is 0.06 s; a value in milliseconds would be 60 or more.
        Assert.InRange(measurement.Value, 0.05, 5.0);
        Assert.True(measurement.TryGetTag(WellKnownTagKeys.Outcome, out var outcome));
        Assert.Equal(WellKnownOutcomes.Success, outcome);
        _metrics.ShouldHaveTagSet(CachingTelemetry.FactoryDurationInstrument, CachingTagKeys.KeyPrefix, WellKnownTagKeys.Outcome);
    }

    [Fact]
    public async Task FactoryException_CountsOneError_WithTheOperationAndTheExceptionFullName_AndAnExceptionDuration()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _cache.GetOrSetAsync<string>(Key("boom"), _ => throw new InvalidOperationException("factory failed"), CachePolicy.Default));

        _metrics.ShouldHaveSum(
            CachingTelemetry.ErrorsInstrument,
            1,
            Tag(CachingTagKeys.Operation, CachingTagValues.OperationFactory),
            Tag(WellKnownTagKeys.ErrorType, typeof(InvalidOperationException).FullName));
        _metrics.ShouldHaveTagSet(CachingTelemetry.ErrorsInstrument, CachingTagKeys.Operation, WellKnownTagKeys.ErrorType);
        _metrics.ShouldHaveMeasurement(
            CachingTelemetry.FactoryDurationInstrument,
            value: null,
            Tag(CachingTagKeys.KeyPrefix, Prefix),
            Tag(WellKnownTagKeys.Outcome, WellKnownOutcomes.Exception));
        _metrics.ShouldUseOutcomeVocabulary(CachingTelemetry.FactoryDurationInstrument);
    }

    [Fact]
    public async Task FactoryCancellation_IsNotAnError_AndRecordsTheCanceledOutcome()
    {
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await _cache.GetOrSetAsync<string>(
                Key("cancelled"),
                async ct =>
                {
                    await Task.Delay(Timeout.Infinite, ct);
                    return "never";
                },
                CachePolicy.Default,
                cts.Token));

        Assert.Equal(0, _metrics.Count(CachingTelemetry.ErrorsInstrument));
        _metrics.ShouldHaveMeasurement(
            CachingTelemetry.FactoryDurationInstrument,
            value: null,
            Tag(CachingTagKeys.KeyPrefix, Prefix),
            Tag(WellKnownTagKeys.Outcome, WellKnownOutcomes.Canceled));
    }

    [Fact]
    public async Task Evictions_AreCounted_WithTheReason()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSharedKernelCaching(o =>
        {
            o.ServiceName = "svc";
            o.L1SizeLimit = 2;
        });
        await using var evictProvider = services.BuildServiceProvider();
        var evictCache = evictProvider.GetRequiredService<ICacheService>();

        for (var i = 0; i < 10; i++)
            await evictCache.SetAsync(Key("evict-" + i), i, CachePolicy.Default);

        Assert.True(await Eventually.HoldsAsync(() => _metrics.Count(CachingTelemetry.EvictionsInstrument) >= 1));
        _metrics.ShouldHaveTagSet(CachingTelemetry.EvictionsInstrument, CachingTagKeys.EvictionReason);
    }

    [Fact]
    public async Task EveryInstrument_FollowsTheMetricConvention_WithItsUnit()
    {
        await _cache.TryGetAsync<string>(Key("convention"));

        _metrics
            .ShouldHaveInstrument(CachingTelemetry.HitsInstrument, "{hit}", InstrumentKind.Counter)
            .ShouldHaveInstrument(CachingTelemetry.MissesInstrument, "{miss}", InstrumentKind.Counter)
            .ShouldHaveInstrument(CachingTelemetry.ErrorsInstrument, "{error}", InstrumentKind.Counter)
            .ShouldHaveInstrument(CachingTelemetry.EvictionsInstrument, "{eviction}", InstrumentKind.Counter)
            .ShouldHaveInstrument(CachingTelemetry.FactoryDurationInstrument, "s", InstrumentKind.Histogram)
            .ShouldFollowMetricConvention();
    }

    [Fact]
    public async Task Meter_IsNamedSharedKernelCaching()
    {
        await _cache.TryGetAsync<string>(Key("version"));

        Assert.NotEmpty(_metrics.Instruments);
        Assert.All(_metrics.Instruments, instrument => Assert.Equal("SharedKernel.Caching", instrument.MeterName));
    }

    [Fact]
    public async Task EveryTagValue_IsFreeOfIds()
    {
        await _cache.SetAsync(Key("secret-id-42"), "v", CachePolicy.Default);
        await _cache.TryGetAsync<string>(Key("secret-id-42"));
        await _cache.GetOrSetAsync(Key("secret-id-43"), _ => ValueTask.FromResult("v"), CachePolicy.Default);

        _metrics.ShouldNotLeak("secret-id-42", "secret-id-43");
    }
}

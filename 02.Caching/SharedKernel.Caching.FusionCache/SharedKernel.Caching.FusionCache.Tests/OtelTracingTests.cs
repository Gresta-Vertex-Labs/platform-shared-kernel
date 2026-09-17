using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Caching.FusionCache.Extensions;
using Xunit;

namespace SharedKernel.Caching.FusionCache.Tests;

/// <summary>
/// Unit tests verifying that <see cref="Implementations.FusionCacheService"/> produces
/// the correct distributed-trace spans under the activity source
/// <c>SharedKernel.Caching</c> (Phase 41, P-304).
///
/// Each test sets up an <see cref="ActivityListener"/> scoped to that test to
/// capture spans started while the listener is active — mirrors
/// <see cref="OtelMetricsTests"/>'s <c>MeterListener</c>-based structure and,
/// like that suite, asserts existence ("a span with these characteristics was
/// produced") rather than exact single-item identity: xUnit's default
/// cross-class test parallelism means other test classes in this assembly may
/// concurrently drive their own <c>ICacheService</c> instances against the same
/// static <see cref="ActivitySource"/>, so an <see cref="ActivityListener"/>
/// registered process-wide can observe spans this test did not itself produce.
/// The captured-span accumulator is a <see cref="ConcurrentBag{T}"/>, not a plain
/// <see cref="List{T}"/> — <see cref="ActivityListener.ActivityStopped"/> can be
/// invoked concurrently from multiple threads under that same cross-class
/// parallelism (see <see cref="OtelMetricsTests"/>'s own
/// <see cref="ConcurrentDictionary{TKey, TValue}"/> fix for the identical hazard).
/// </summary>
public sealed class OtelTracingTests : IDisposable
{
    private readonly ServiceProvider _provider;
    private readonly ICacheService _cache;

    public OtelTracingTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSharedKernelCaching(o => o.ServiceName = "svc");
        _provider = services.BuildServiceProvider();
        _cache = _provider.GetRequiredService<ICacheService>();
    }

    public void Dispose() => _provider.Dispose();

    // -------------------------------------------------------------------------
    // Helper: build an ActivityListener that captures completed spans from the
    // "SharedKernel.Caching" activity source.
    // -------------------------------------------------------------------------

    private static ActivityListener BuildListener(out ConcurrentBag<Activity> activities)
    {
        var captured = new ConcurrentBag<Activity>();
        activities = captured;

        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "SharedKernel.Caching",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => captured.Add(activity),
        };

        ActivitySource.AddActivityListener(listener);
        return listener;
    }

    // -------------------------------------------------------------------------
    // Helper: does the captured set contain a span matching every criterion?
    // -------------------------------------------------------------------------

    private static bool HasMatchingSpan(
        IEnumerable<Activity> activities,
        string operationName,
        ActivityKind kind,
        string keyPrefix,
        string? outcome) =>
        activities.Any(a =>
            a.OperationName == operationName &&
            a.Kind == kind &&
            (string?)a.GetTagItem("cache.key_prefix") == keyPrefix &&
            (string?)a.GetTagItem("cache.outcome") == outcome);

    // -------------------------------------------------------------------------
    // OT-02: TryGetAsync hit — span "cache.get", ActivityKind.Client, outcome "hit"
    // -------------------------------------------------------------------------

    [Fact]
    public async Task TryGetAsync_Hit_ProducesSpanWithHitOutcome()
    {
        const string key = "svc:entity:get-hit-" + nameof(TryGetAsync_Hit_ProducesSpanWithHitOutcome);

        // Populate the cache first, outside the listener's capture window.
        await _cache.SetAsync(key, "value", CachePolicy.Default);

        using var listener = BuildListener(out var activities);

        var result = await _cache.TryGetAsync<string>(key);

        Assert.Equal("value", result.Value);
        Assert.True(
            HasMatchingSpan(activities, "cache.get", ActivityKind.Client, "svc:entity", "hit"),
            "Expected a cache.get span (ActivityKind.Client) tagged cache.key_prefix=svc:entity, cache.outcome=hit.");
    }

    // -------------------------------------------------------------------------
    // OT-02: TryGetAsync miss — span "cache.get", ActivityKind.Client, outcome "miss"
    // -------------------------------------------------------------------------

    [Fact]
    public async Task TryGetAsync_Miss_ProducesSpanWithMissOutcome()
    {
        var key = "svc:entity:get-miss-" + Guid.NewGuid();

        using var listener = BuildListener(out var activities);

        var result = await _cache.TryGetAsync<string>(key);

        Assert.False(result.IsHit);
        Assert.True(
            HasMatchingSpan(activities, "cache.get", ActivityKind.Client, "svc:entity", "miss"),
            "Expected a cache.get span (ActivityKind.Client) tagged cache.key_prefix=svc:entity, cache.outcome=miss.");
    }

    // -------------------------------------------------------------------------
    // OT-03: SetAsync — span "cache.set", ActivityKind.Client, key_prefix only
    // (no cache.outcome tag — a write has no hit/miss concept)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SetAsync_ProducesSpanWithKeyPrefixOnly()
    {
        var key = "svc:entity:set-" + Guid.NewGuid();

        using var listener = BuildListener(out var activities);

        await _cache.SetAsync(key, "value", CachePolicy.Default);

        Assert.True(
            HasMatchingSpan(activities, "cache.set", ActivityKind.Client, "svc:entity", outcome: null),
            "Expected a cache.set span (ActivityKind.Client) tagged cache.key_prefix=svc:entity with no cache.outcome tag.");
    }

    // -------------------------------------------------------------------------
    // OT-04: GetOrSetAsync hit — span "cache.get_or_set", outcome "hit"; the
    // factory must not run when FusionCache satisfies the request from cache.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetOrSetAsync_Hit_ProducesSpanWithHitOutcome()
    {
        var key = "svc:entity:getorset-hit-" + Guid.NewGuid();

        // Populate the cache first, outside the listener's capture window.
        await _cache.SetAsync(key, "cached-value", CachePolicy.Default);

        using var listener = BuildListener(out var activities);

        var factoryInvoked = false;
        var result = await _cache.GetOrSetAsync<string>(
            key,
            _ =>
            {
                factoryInvoked = true;
                return ValueTask.FromResult("factory-value");
            },
            CachePolicy.Default);

        Assert.Equal("cached-value", result);
        Assert.False(factoryInvoked, "Factory must not run on a cache hit.");
        Assert.True(
            HasMatchingSpan(activities, "cache.get_or_set", ActivityKind.Client, "svc:entity", "hit"),
            "Expected a cache.get_or_set span (ActivityKind.Client) tagged cache.key_prefix=svc:entity, cache.outcome=hit.");
    }

    // -------------------------------------------------------------------------
    // OT-04: GetOrSetAsync miss — span "cache.get_or_set", outcome "miss"; the
    // factory ran because no prior entry existed for the key.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetOrSetAsync_Miss_ProducesSpanWithMissOutcome()
    {
        var key = "svc:entity:getorset-miss-" + Guid.NewGuid();

        using var listener = BuildListener(out var activities);

        var result = await _cache.GetOrSetAsync<string>(
            key,
            _ => ValueTask.FromResult("factory-value"),
            CachePolicy.Default);

        Assert.Equal("factory-value", result);
        Assert.True(
            HasMatchingSpan(activities, "cache.get_or_set", ActivityKind.Client, "svc:entity", "miss"),
            "Expected a cache.get_or_set span (ActivityKind.Client) tagged cache.key_prefix=svc:entity, cache.outcome=miss.");
    }

    // -------------------------------------------------------------------------
    // ActivitySource identity — name and version match the existing Meter's
    // instrumentation scope.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ActivitySource_HasCorrectNameAndVersion()
    {
        using var listener = BuildListener(out var activities);

        await _cache.SetAsync("svc:entity:source-identity-" + Guid.NewGuid(), "value", CachePolicy.Default);

        Assert.Contains(activities, a =>
            a.OperationName == "cache.set" &&
            a.Source.Name == "SharedKernel.Caching" &&
            a.Source.Version == "1.0");
    }
}

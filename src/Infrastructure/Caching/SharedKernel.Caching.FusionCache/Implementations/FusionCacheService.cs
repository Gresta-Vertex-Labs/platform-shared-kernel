using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Caching.FusionCache.Diagnostics;
using SharedKernel.Primitives.Propagation;
using SharedKernel.Primitives.Telemetry;
using ZiggyCreatures.Caching.Fusion;
using ZiggyCreatures.Caching.Fusion.Events;

namespace SharedKernel.Caching.FusionCache.Implementations;

/// <summary>
/// <see cref="ICacheService"/> implementation backed by FusionCache: an in-process memory layer
/// plus the optional Redis distributed layer and backplane added by <c>AddRedisL2</c>.
/// </summary>
/// <remarks>
/// Emits metrics under the meter <see cref="CachingTelemetry.MeterName"/> (created through <see cref="IMeterFactory"/>)
/// and spans under the activity source <see cref="CachingTelemetry.SourceName"/>, both versioned with this assembly. Tags
/// carry only <c>{service}:{entity}</c> key prefixes: never an id and never a tenant.
/// </remarks>
internal sealed class FusionCacheService : ICacheService
{
    // Batch reads and writes fan out per-key L2 round-trips instead of awaiting them one by one.
    // Fixed rather than configurable until telemetry shows a workload for which 16 is wrong.
    private const int MaxBatchConcurrency = 16;

    // Span names are "{capability} {operation}", lowercase.
    private const string SpanGet = "caching get";
    private const string SpanGetOrSet = "caching get_or_set";
    private const string SpanSet = "caching set";

    // The only static telemetry state: an ActivitySource is process-wide by design. The meter and its instruments
    // are created per instance through IMeterFactory.
    private static readonly ActivitySource _activitySource =
        new(CachingTelemetry.SourceName, TelemetryVersion.Of(typeof(FusionCacheService).Assembly));

    private readonly IFusionCache _cache;
    private readonly ILogger<FusionCacheService> _logger;
    private readonly Counter<long> _cacheHits;
    private readonly Counter<long> _cacheMisses;
    private readonly Histogram<double> _factoryDuration;
    private readonly Counter<long> _cacheErrors;
    private readonly Counter<long> _cacheEvictions;

    public FusionCacheService(IFusionCache cache, IMeterFactory meterFactory, ILogger<FusionCacheService> logger)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(meterFactory);
        ArgumentNullException.ThrowIfNull(logger);

        _cache = cache;
        _logger = logger;

        var meter = meterFactory.Create(new MeterOptions(CachingTelemetry.MeterName)
        {
            Version = TelemetryVersion.Of(typeof(FusionCacheService).Assembly),
        });

        _cacheHits = meter.CreateCounter<long>(
            CachingTelemetry.HitsInstrument,
            unit: "{hit}",
            description: "Reads answered from a cache layer. Tags: caching.key_prefix ({service}:{entity}), caching.level (l1 or l2).");

        _cacheMisses = meter.CreateCounter<long>(
            CachingTelemetry.MissesInstrument,
            unit: "{miss}",
            description: "Reads no cache layer could answer: a TryGet miss or a GetOrSet factory run. Tag: caching.key_prefix.");

        _factoryDuration = meter.CreateHistogram<double>(
            CachingTelemetry.FactoryDurationInstrument,
            unit: "s",
            description: "Factory execution duration in seconds. Tags: caching.key_prefix, outcome (success, exception or canceled).");

        _cacheErrors = meter.CreateCounter<long>(
            CachingTelemetry.ErrorsInstrument,
            unit: "{error}",
            description: "Failed cache writes and factory runs. Tags: caching.operation (set or factory), error.type (exception full name).");

        _cacheEvictions = meter.CreateCounter<long>(
            CachingTelemetry.EvictionsInstrument,
            unit: "{eviction}",
            description: "Memory-cache evictions. Tag: caching.eviction_reason.");

        // Hits come from FusionCache events, so the answering layer is known. Misses are counted at the
        // call sites: a memory miss that the distributed layer answers is not a miss.
        _cache.Events.Memory.Hit += OnMemoryHit;
        _cache.Events.Distributed.Hit += OnDistributedHit;
        _cache.Events.Memory.Eviction += OnMemoryEviction;
    }

    private void OnMemoryHit(object? sender, FusionCacheEntryHitEventArgs e) =>
        RecordHit(e.Key, CachingTagValues.LevelMemory);

    private void OnDistributedHit(object? sender, FusionCacheEntryHitEventArgs e) =>
        RecordHit(e.Key, CachingTagValues.LevelDistributed);

    private void RecordHit(string key, string level) =>
        _cacheHits.Add(
            1,
            new KeyValuePair<string, object?>(CachingTagKeys.KeyPrefix, ExtractKeyPrefix(key)),
            new KeyValuePair<string, object?>(CachingTagKeys.Level, level));

    private void OnMemoryEviction(object? sender, FusionCacheEntryEvictionEventArgs e) =>
        _cacheEvictions.Add(
            1,
            new KeyValuePair<string, object?>(CachingTagKeys.EvictionReason, e.Reason.ToString()));

    private void RecordError(string operation, Exception exception) =>
        _cacheErrors.Add(
            1,
            new KeyValuePair<string, object?>(CachingTagKeys.Operation, operation),
            new KeyValuePair<string, object?>(WellKnownTagKeys.ErrorType, exception.GetType().FullName ?? exception.GetType().Name));

    // The low-cardinality, tenant-free part of a key for metrics, traces and logs: {service}:{entity}
    // from both {service}:{entity}:{id}[:...] and {service}:@{tenant}:{entity}:{id}[:...].
    internal static string ExtractKeyPrefix(string key)
    {
        if (string.IsNullOrEmpty(key))
            return string.Empty;

        var segments = key.Split(CacheKeyFormat.Separator, 4);
        if (segments.Length < 2)
            return segments[0];

        if (segments[1].StartsWith(CacheKeyFormat.TenantMarker))
            return segments.Length > 2 ? $"{segments[0]}:{segments[2]}" : segments[0];

        return $"{segments[0]}:{segments[1]}";
    }

    // A tag for logs: a tenant tag @{tenant}:{tag} becomes @tenant:{tag}, so tenant ids never reach logs.
    internal static string DescribeTag(string tag)
    {
        if (string.IsNullOrEmpty(tag) || tag[0] != CacheKeyFormat.TenantMarker)
            return tag;

        var separator = tag.IndexOf(CacheKeyFormat.Separator, StringComparison.Ordinal);
        return separator < 0 ? "@tenant" : "@tenant" + tag[separator..];
    }

    public async ValueTask<CacheLookup<T>> TryGetAsync<T>(string key, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        using var activity = _activitySource.StartActivity(SpanGet, ActivityKind.Client);
        var keyPrefix = ExtractKeyPrefix(key);
        activity?.SetTag(CachingTagKeys.KeyPrefix, keyPrefix);

        MaybeValue<T> result;
        try
        {
            result = await _cache.TryGetAsync<T>(key, token: ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            activity.RecordException(ex);
            throw;
        }

        if (!result.HasValue)
            RecordMiss(keyPrefix);

        activity?.SetTag(CachingTagKeys.Result, result.HasValue ? CachingTagValues.ResultHit : CachingTagValues.ResultMiss);

        return result.HasValue ? CacheLookup<T>.Hit(result.Value) : CacheLookup<T>.Miss;
    }

    public async ValueTask<IReadOnlyDictionary<string, CacheLookup<T>>> TryGetManyAsync<T>(
        IEnumerable<string> keys,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(keys);

        var distinctKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in keys)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key, nameof(keys));
            distinctKeys.Add(key);
        }

        var result = new ConcurrentDictionary<string, CacheLookup<T>>(StringComparer.Ordinal);

        await Parallel.ForEachAsync(
            distinctKeys,
            new ParallelOptions { MaxDegreeOfParallelism = MaxBatchConcurrency, CancellationToken = ct },
            async (key, token) =>
            {
                var entry = await _cache.TryGetAsync<T>(key, token: token).ConfigureAwait(false);
                result[key] = entry.HasValue ? CacheLookup<T>.Hit(entry.Value) : CacheLookup<T>.Miss;

                if (!entry.HasValue)
                    RecordMiss(ExtractKeyPrefix(key));
            }).ConfigureAwait(false);

        return result;
    }

    public ValueTask<T> GetOrSetAsync<T>(
        string key,
        Func<CancellationToken, ValueTask<T>> factory,
        CachePolicy policy,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(factory);
        return GetOrSetAsync(key, (_, token) => factory(token), policy, ct);
    }

    public async ValueTask<T> GetOrSetAsync<T>(
        string key,
        Func<CacheFactoryContext, CancellationToken, ValueTask<T>> factory,
        CachePolicy policy,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(policy);

        using var activity = _activitySource.StartActivity(SpanGetOrSet, ActivityKind.Client);
        var keyPrefix = ExtractKeyPrefix(key);
        activity?.SetTag(CachingTagKeys.KeyPrefix, keyPrefix);

        var entryOptions = BuildEntryOptions(policy, _cache.DefaultEntryOptions);
        var tags = policy.Tags.Count > 0 ? policy.Tags : null;

        // Set from inside the factory: a miss runs it, a hit (L1 or L2) does not.
        var factoryInvoked = false;

        var result = await _cache.GetOrSetAsync<T>(
            key,
            async (fusionContext, token) =>
            {
                factoryInvoked = true;
                FusionCacheLog.FactoryInvoked(_logger, keyPrefix);
                _cacheMisses.Add(1, new KeyValuePair<string, object?>(CachingTagKeys.KeyPrefix, keyPrefix));
                var startedAt = Stopwatch.GetTimestamp();
                try
                {
                    var context = new CacheFactoryContext(key, policy);
                    var value = await factory(context, token).ConfigureAwait(false);
                    RecordFactoryDuration(keyPrefix, startedAt, WellKnownOutcomes.Success);

                    ApplyFactoryDecision(context, fusionContext.Options);
                    return value;
                }
                catch (Exception ex)
                {
                    // Cancellation is not an error: it records the duration with the canceled outcome and nothing else.
                    var outcome = activity.RecordException(ex);
                    RecordFactoryDuration(keyPrefix, startedAt, outcome);
                    if (outcome == WellKnownOutcomes.Exception)
                        RecordError(CachingTagValues.OperationFactory, ex);
                    throw;
                }
            },
            MaybeValue<T>.None,
            entryOptions,
            tags,
            token: ct).ConfigureAwait(false);

        activity?.SetTag(CachingTagKeys.Result, factoryInvoked ? CachingTagValues.ResultMiss : CachingTagValues.ResultHit);

        return result;
    }

    public async ValueTask SetAsync<T>(string key, T value, CachePolicy policy, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(policy);

        using var activity = _activitySource.StartActivity(SpanSet, ActivityKind.Client);
        var keyPrefix = ExtractKeyPrefix(key);
        activity?.SetTag(CachingTagKeys.KeyPrefix, keyPrefix);

        var entryOptions = BuildEntryOptions(policy, _cache.DefaultEntryOptions);
        var tags = policy.Tags.Count > 0 ? policy.Tags : null;

        try
        {
            await _cache.SetAsync(key, value, entryOptions, tags, token: ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            if (activity.RecordException(ex) == WellKnownOutcomes.Exception)
                RecordError(CachingTagValues.OperationSet, ex);
            throw;
        }

        FusionCacheLog.CacheSet(_logger, keyPrefix);
    }

    public async ValueTask SetManyAsync<T>(
        IReadOnlyDictionary<string, T> entries,
        CachePolicy policy,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(policy);

        foreach (var key in entries.Keys)
            ArgumentException.ThrowIfNullOrWhiteSpace(key, nameof(entries));

        var entryOptions = BuildEntryOptions(policy, _cache.DefaultEntryOptions);
        var tags = policy.Tags.Count > 0 ? policy.Tags : null;

        await Parallel.ForEachAsync(
            entries,
            new ParallelOptions { MaxDegreeOfParallelism = MaxBatchConcurrency, CancellationToken = ct },
            async (entry, token) =>
            {
                await _cache.SetAsync(entry.Key, entry.Value, entryOptions, tags, token: token).ConfigureAwait(false);
                FusionCacheLog.CacheSet(_logger, ExtractKeyPrefix(entry.Key));
            }).ConfigureAwait(false);
    }

    public async ValueTask RemoveAsync(string key, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        await _cache.RemoveAsync(key, token: ct).ConfigureAwait(false);
        FusionCacheLog.CacheRemoved(_logger, ExtractKeyPrefix(key));
    }

    public async ValueTask ExpireAsync(string key, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        await _cache.ExpireAsync(key, token: ct).ConfigureAwait(false);
        FusionCacheLog.CacheExpired(_logger, ExtractKeyPrefix(key));
    }

    public async ValueTask RemoveByTagAsync(string tag, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);

        await _cache.RemoveByTagAsync(tag, token: ct).ConfigureAwait(false);
        FusionCacheLog.CacheTagRemoved(_logger, DescribeTag(tag));
    }

    public async ValueTask RemoveByTagsAsync(IEnumerable<string> tags, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(tags);

        var distinctTags = new HashSet<string>(StringComparer.Ordinal);
        foreach (var tag in tags)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(tag, nameof(tags));
            distinctTags.Add(tag);
        }

        if (distinctTags.Count == 0)
            return;

        await _cache.RemoveByTagAsync(distinctTags, token: ct).ConfigureAwait(false);

        foreach (var tag in distinctTags)
            FusionCacheLog.CacheTagRemoved(_logger, DescribeTag(tag));
    }

    public async ValueTask ClearAsync(CancellationToken ct = default)
    {
        await _cache.ClearAsync(allowFailSafe: false, token: ct).ConfigureAwait(false);
        FusionCacheLog.CacheCleared(_logger);
    }

    private void RecordMiss(string keyPrefix)
    {
        FusionCacheLog.CacheMiss(_logger, keyPrefix);
        _cacheMisses.Add(1, new KeyValuePair<string, object?>(CachingTagKeys.KeyPrefix, keyPrefix));
    }

    // Seconds, tagged with the outcome the shared recorder returned for the factory run.
    private void RecordFactoryDuration(string keyPrefix, long startedAt, string outcome) =>
        _factoryDuration.Record(
            Stopwatch.GetElapsedTime(startedAt).TotalSeconds,
            new KeyValuePair<string, object?>(CachingTagKeys.KeyPrefix, keyPrefix),
            new KeyValuePair<string, object?>(WellKnownTagKeys.Outcome, outcome));

    // Converts a CachePolicy to FusionCache entry options, starting from the cache's default entry
    // options, which carry the service-wide CachingOptions defaults (distributed timeouts, fail-safe throttle).
    internal static FusionCacheEntryOptions BuildEntryOptions(CachePolicy policy, FusionCacheEntryOptions defaults)
    {
        var options = defaults.Duplicate();
        options.IsFailSafeEnabled = policy.IsFailSafeEnabled;
        // Size = 1 so every entry counts as one unit against CachingOptions.L1SizeLimit (an entry count, not bytes).
        options.Size = 1;

        SetDurations(options, policy.L1Duration, policy.L2Duration);

        if (policy.FailSafeMaxDuration is { } failSafeMax)
        {
            options.FailSafeMaxDuration = failSafeMax;
            options.DistributedCacheFailSafeMaxDuration = failSafeMax;
        }

        if (policy.FactorySoftTimeout is { } softTimeout)
            options.FactorySoftTimeout = softTimeout;

        if (policy.FactoryHardTimeout is { } hardTimeout)
            options.FactoryHardTimeout = hardTimeout;

        options.EagerRefreshThreshold = policy.EagerRefreshThreshold is { } threshold ? (float)threshold : null;

        if (policy.JitterMaxDuration is { } jitter)
            options.JitterMaxDuration = jitter;

        if (policy.IsLocalOnly)
        {
            // A process-local entry must neither reach L2 nor tell other nodes to evict their own copies.
            options.SkipDistributedCacheRead = true;
            options.SkipDistributedCacheWrite = true;
            options.SkipBackplaneNotifications = true;
        }

        return options;
    }

    // Applies what the factory decided through its CacheFactoryContext to the options FusionCache
    // uses for this one write.
    internal static void ApplyFactoryDecision(CacheFactoryContext context, FusionCacheEntryOptions options)
    {
        if (context.IsCachingSkipped)
        {
            options.SkipMemoryCacheWrite = true;
            options.SkipDistributedCacheWrite = true;
            options.SkipBackplaneNotifications = true;
            return;
        }

        if (context.L1DurationOverride is { } l1 && context.L2DurationOverride is { } l2)
            SetDurations(options, l1, l2);
    }

    private static void SetDurations(FusionCacheEntryOptions options, TimeSpan l1Duration, TimeSpan l2Duration)
    {
        // TimeSpan.MaxValue means "never expires by time"; FusionCache represents that with
        // DateTimeOffset.MaxValue instead of overflowing now + MaxValue.
        if (l1Duration == TimeSpan.MaxValue)
            options.SetDurationInfinite();
        else
            options.Duration = l1Duration;

        if (l2Duration == TimeSpan.MaxValue)
            options.SetDistributedCacheDurationInfinite();
        else
            options.DistributedCacheDuration = l2Duration;
    }
}

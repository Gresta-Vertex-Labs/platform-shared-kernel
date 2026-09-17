using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Primitives.Logging;
using ZiggyCreatures.Caching.Fusion;

namespace SharedKernel.Caching.FusionCache.Implementations;

/// <summary>
/// <see cref="ICacheService"/> implementation backed by FusionCache: an in-process memory layer
/// plus the optional Redis distributed layer and backplane added by <c>AddRedisL2</c>.
/// </summary>
/// <remarks>
/// Emits <see cref="System.Diagnostics.Metrics"/> instruments under the meter
/// <c>SharedKernel.Caching</c> (version <c>1.0</c>), and distributed-trace spans under the
/// identically-named/versioned <see cref="ActivitySource"/> — deliberately the same
/// instrumentation-scope name and version as the meter, since OTel treats the trace and metric
/// surfaces of one component as one instrumentation scope.
/// </remarks>
internal sealed partial class FusionCacheService : ICacheService
{
    // Batch reads and writes fan out per-key L2 round-trips instead of awaiting them one by one.
    // Fixed rather than configurable until telemetry shows a workload for which 16 is wrong.
    private const int MaxBatchConcurrency = 16;

    // OTel metrics and tracing — static, shared across instances; negligible cost without a listener.
    private static readonly Meter _meter = new("SharedKernel.Caching", "1.0");

    private static readonly Counter<long> _cacheHits =
        _meter.CreateCounter<long>(
            "cache.hits",
            description: "Number of cache hits. Tag cache.key_prefix = {service}:{entity}.");

    private static readonly Counter<long> _cacheMisses =
        _meter.CreateCounter<long>(
            "cache.misses",
            description: "Number of cache misses. Tag cache.key_prefix = {service}:{entity}.");

    private static readonly Histogram<double> _factoryDuration =
        _meter.CreateHistogram<double>(
            "cache.factory.duration",
            unit: "ms",
            description: "Factory execution duration in milliseconds. Tag cache.key_prefix = {service}:{entity}.");

    private static readonly Counter<long> _cacheErrors =
        _meter.CreateCounter<long>(
            "cache.errors",
            description: "Number of cache errors. Tag cache.error_type = exception type name.");

    private static readonly Counter<long> _cacheEvictions =
        _meter.CreateCounter<long>(
            "cache.evictions",
            description: "Number of L1 memory evictions. Tag cache.eviction_reason = EvictionReason name.");

    private static readonly ActivitySource _activitySource = new("SharedKernel.Caching", "1.0");

    private readonly IFusionCache _cache;
    private readonly ILogger<FusionCacheService> _logger;

    public FusionCacheService(IFusionCache cache, ILogger<FusionCacheService> logger)
    {
        _cache = cache;
        _logger = logger;

        // Subscribe to FusionCache memory events for hit/miss/eviction instead of instrumenting
        // every call site.
        _cache.Events.Memory.Hit += OnMemoryHit;
        _cache.Events.Memory.Miss += OnMemoryMiss;
        _cache.Events.Memory.Eviction += OnMemoryEviction;
    }

    private static void OnMemoryHit(object? sender, ZiggyCreatures.Caching.Fusion.Events.FusionCacheEntryHitEventArgs e)
    {
        var prefix = ExtractKeyPrefix(e.Key);
        _cacheHits.Add(1,
            new KeyValuePair<string, object?>("cache.key_prefix", prefix),
            new KeyValuePair<string, object?>("cache.level", "l1"));
    }

    private static void OnMemoryMiss(object? sender, ZiggyCreatures.Caching.Fusion.Events.FusionCacheEntryEventArgs e)
    {
        var prefix = ExtractKeyPrefix(e.Key);
        _cacheMisses.Add(1,
            new KeyValuePair<string, object?>("cache.key_prefix", prefix));
    }

    private static void OnMemoryEviction(object? sender, ZiggyCreatures.Caching.Fusion.Events.FusionCacheEntryEvictionEventArgs e)
    {
        _cacheEvictions.Add(1,
            new KeyValuePair<string, object?>("cache.eviction_reason", e.Reason.ToString()));
    }

    // {service}:{entity} from {service}:{entity}:{id}[:...], or {service}:@{tenant} for a tenant key.
    // Never includes the id segment, to avoid high-cardinality metric labels.
    internal static string ExtractKeyPrefix(string key)
    {
        if (string.IsNullOrEmpty(key))
            return string.Empty;

        var firstColon = key.IndexOf(':', StringComparison.Ordinal);
        if (firstColon < 0)
            return key;

        var secondColon = key.IndexOf(':', firstColon + 1);
        return secondColon < 0 ? key : key[..secondColon];
    }

    public async ValueTask<CacheLookup<T>> TryGetAsync<T>(string key, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        using var activity = _activitySource.StartActivity("cache.get", ActivityKind.Client);
        var keyPrefix = ExtractKeyPrefix(key);
        activity?.SetTag("cache.key_prefix", keyPrefix);

        var result = await _cache.TryGetAsync<T>(key, token: ct).ConfigureAwait(false);

        if (!result.HasValue)
        {
            Log.CacheMiss(_logger, key);
            // TryGetAsync does not raise FusionCache's Memory.Miss event.
            _cacheMisses.Add(1, new KeyValuePair<string, object?>("cache.key_prefix", keyPrefix));
        }

        activity?.SetTag("cache.outcome", result.HasValue ? "hit" : "miss");

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
                    Log.CacheMiss(_logger, key);
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

        using var activity = _activitySource.StartActivity("cache.get_or_set", ActivityKind.Client);
        var keyPrefix = ExtractKeyPrefix(key);
        activity?.SetTag("cache.key_prefix", keyPrefix);

        var entryOptions = BuildEntryOptions(policy);
        var tags = policy.Tags.Count > 0 ? policy.Tags : null;

        // Set from inside the factory: a miss runs it, a hit (L1 or L2) does not.
        var factoryInvoked = false;

        var result = await _cache.GetOrSetAsync<T>(
            key,
            async (fusionContext, token) =>
            {
                factoryInvoked = true;
                Log.FactoryInvoked(_logger, key);
                var sw = Stopwatch.StartNew();
                try
                {
                    var context = new CacheFactoryContext(key, policy);
                    var value = await factory(context, token).ConfigureAwait(false);
                    sw.Stop();
                    _factoryDuration.Record(
                        sw.Elapsed.TotalMilliseconds,
                        new KeyValuePair<string, object?>("cache.key_prefix", keyPrefix));

                    ApplyFactoryDecision(context, fusionContext.Options);
                    return value;
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    _cacheErrors.Add(1,
                        new KeyValuePair<string, object?>("cache.error_type", ex.GetType().Name));
                    activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                    throw;
                }
            },
            MaybeValue<T>.None,
            entryOptions,
            tags,
            token: ct).ConfigureAwait(false);

        activity?.SetTag("cache.outcome", factoryInvoked ? "miss" : "hit");

        return result;
    }

    public async ValueTask SetAsync<T>(string key, T value, CachePolicy policy, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(policy);

        using var activity = _activitySource.StartActivity("cache.set", ActivityKind.Client);
        activity?.SetTag("cache.key_prefix", ExtractKeyPrefix(key));

        var entryOptions = BuildEntryOptions(policy);
        var tags = policy.Tags.Count > 0 ? policy.Tags : null;

        try
        {
            await _cache.SetAsync(key, value, entryOptions, tags, token: ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _cacheErrors.Add(1,
                new KeyValuePair<string, object?>("cache.error_type", ex.GetType().Name));
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }

        Log.CacheSet(_logger, key);
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

        var entryOptions = BuildEntryOptions(policy);
        var tags = policy.Tags.Count > 0 ? policy.Tags : null;

        await Parallel.ForEachAsync(
            entries,
            new ParallelOptions { MaxDegreeOfParallelism = MaxBatchConcurrency, CancellationToken = ct },
            async (entry, token) =>
            {
                await _cache.SetAsync(entry.Key, entry.Value, entryOptions, tags, token: token).ConfigureAwait(false);
                Log.CacheSet(_logger, entry.Key);
            }).ConfigureAwait(false);
    }

    public async ValueTask RemoveAsync(string key, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        await _cache.RemoveAsync(key, token: ct).ConfigureAwait(false);
        Log.CacheRemoved(_logger, key);
    }

    public async ValueTask ExpireAsync(string key, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        await _cache.ExpireAsync(key, token: ct).ConfigureAwait(false);
        Log.CacheExpired(_logger, key);
    }

    public async ValueTask RemoveByTagAsync(string tag, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);

        await _cache.RemoveByTagAsync(tag, token: ct).ConfigureAwait(false);
        Log.CacheTagRemoved(_logger, tag);
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
            Log.CacheTagRemoved(_logger, tag);
    }

    public async ValueTask ClearAsync(CancellationToken ct = default)
    {
        await _cache.ClearAsync(allowFailSafe: false, token: ct).ConfigureAwait(false);
        Log.CacheCleared(_logger);
    }

    // Converts a CachePolicy to FusionCache entry options.
    internal static FusionCacheEntryOptions BuildEntryOptions(CachePolicy policy)
    {
        var options = new FusionCacheEntryOptions
        {
            IsFailSafeEnabled = policy.IsFailSafeEnabled,
            // Size = 1 so every entry counts as one unit against CachingOptions.L1SizeLimit (an entry count, not bytes).
            Size = 1,
        };

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

        if (policy.EagerRefreshThreshold is { } threshold)
            options.EagerRefreshThreshold = (float)threshold;

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

    // Source-generated log methods for hot-path logging.
    private static partial class Log
    {
        [LoggerMessage(EventId = LoggingEventIdRanges.Caching + 10, Level = LogLevel.Debug,
            Message = "Cache miss for key '{Key}'")]
        internal static partial void CacheMiss(ILogger logger, string key);

        [LoggerMessage(EventId = LoggingEventIdRanges.Caching + 11, Level = LogLevel.Debug,
            Message = "Cache set for key '{Key}'")]
        internal static partial void CacheSet(ILogger logger, string key);

        [LoggerMessage(EventId = LoggingEventIdRanges.Caching + 12, Level = LogLevel.Debug,
            Message = "Cache factory invoked for key '{Key}'")]
        internal static partial void FactoryInvoked(ILogger logger, string key);

        [LoggerMessage(EventId = LoggingEventIdRanges.Caching + 13, Level = LogLevel.Debug,
            Message = "Cache entry removed for key '{Key}'")]
        internal static partial void CacheRemoved(ILogger logger, string key);

        [LoggerMessage(EventId = LoggingEventIdRanges.Caching + 14, Level = LogLevel.Debug,
            Message = "Cache entries removed for tag '{Tag}'")]
        internal static partial void CacheTagRemoved(ILogger logger, string tag);

        [LoggerMessage(EventId = LoggingEventIdRanges.Caching + 16, Level = LogLevel.Debug,
            Message = "Cache entry expired for key '{Key}'")]
        internal static partial void CacheExpired(ILogger logger, string key);

        [LoggerMessage(EventId = LoggingEventIdRanges.Caching + 17, Level = LogLevel.Warning,
            Message = "Cache cleared: every entry of this cache was removed")]
        internal static partial void CacheCleared(ILogger logger);
    }
}

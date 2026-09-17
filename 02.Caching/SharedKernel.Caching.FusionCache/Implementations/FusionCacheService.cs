using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Reflection;
using Microsoft.Extensions.Logging;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Primitives.Logging;
using ZiggyCreatures.Caching.Fusion;
using ZiggyCreatures.Caching.Fusion.Events;

namespace SharedKernel.Caching.FusionCache.Implementations;

/// <summary>
/// <see cref="ICacheService"/> implementation backed by FusionCache: an in-process memory layer
/// plus the optional Redis distributed layer and backplane added by <c>AddRedisL2</c>.
/// </summary>
/// <remarks>
/// Emits metrics under the meter <c>SharedKernel.Caching</c> and spans under the activity source of
/// the same name, both versioned with this assembly. Tags carry only <c>{service}:{entity}</c> key
/// prefixes: never an id and never a tenant.
/// </remarks>
internal sealed partial class FusionCacheService : ICacheService
{
    // Batch reads and writes fan out per-key L2 round-trips instead of awaiting them one by one.
    // Fixed rather than configurable until telemetry shows a workload for which 16 is wrong.
    private const int MaxBatchConcurrency = 16;

    // The names stay "SharedKernel.Caching", which 13.ServiceDefaults' WithCachingTelemetry subscribes to.
    private const string InstrumentationName = "SharedKernel.Caching";

    private static readonly string InstrumentationVersion =
        typeof(FusionCacheService).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(FusionCacheService).Assembly.GetName().Version?.ToString()
        ?? "0.0.0";

    // Static and shared across instances; negligible cost without a listener.
    private static readonly Meter _meter = new(InstrumentationName, InstrumentationVersion);

    private static readonly Counter<long> _cacheHits =
        _meter.CreateCounter<long>(
            "cache.hits",
            description: "Reads answered from the cache. Tags: cache.key_prefix ({service}:{entity}), cache.level (l1 or l2).");

    private static readonly Counter<long> _cacheMisses =
        _meter.CreateCounter<long>(
            "cache.misses",
            description: "Reads no cache layer could answer: a TryGet miss or a GetOrSet factory run. Tag: cache.key_prefix.");

    private static readonly Histogram<double> _factoryDuration =
        _meter.CreateHistogram<double>(
            "cache.factory.duration",
            unit: "ms",
            description: "Factory execution duration in milliseconds. Tag: cache.key_prefix.");

    private static readonly Counter<long> _cacheErrors =
        _meter.CreateCounter<long>(
            "cache.errors",
            description: "Failed cache writes and factory runs. Tag: cache.error_type (exception type name).");

    private static readonly Counter<long> _cacheEvictions =
        _meter.CreateCounter<long>(
            "cache.evictions",
            description: "Memory-cache evictions. Tag: cache.eviction_reason.");

    private static readonly ActivitySource _activitySource = new(InstrumentationName, InstrumentationVersion);

    private readonly IFusionCache _cache;
    private readonly ILogger<FusionCacheService> _logger;

    public FusionCacheService(IFusionCache cache, ILogger<FusionCacheService> logger)
    {
        _cache = cache;
        _logger = logger;

        // Hits come from FusionCache events, so the answering layer is known. Misses are counted at the
        // call sites: a memory miss that the distributed layer answers is not a miss.
        _cache.Events.Memory.Hit += OnMemoryHit;
        _cache.Events.Distributed.Hit += OnDistributedHit;
        _cache.Events.Memory.Eviction += OnMemoryEviction;
    }

    private static void OnMemoryHit(object? sender, FusionCacheEntryHitEventArgs e) =>
        _cacheHits.Add(1,
            new KeyValuePair<string, object?>("cache.key_prefix", ExtractKeyPrefix(e.Key)),
            new KeyValuePair<string, object?>("cache.level", "l1"));

    private static void OnDistributedHit(object? sender, FusionCacheEntryHitEventArgs e) =>
        _cacheHits.Add(1,
            new KeyValuePair<string, object?>("cache.key_prefix", ExtractKeyPrefix(e.Key)),
            new KeyValuePair<string, object?>("cache.level", "l2"));

    private static void OnMemoryEviction(object? sender, FusionCacheEntryEvictionEventArgs e) =>
        _cacheEvictions.Add(1,
            new KeyValuePair<string, object?>("cache.eviction_reason", e.Reason.ToString()));

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

        using var activity = _activitySource.StartActivity("cache.get", ActivityKind.Client);
        var keyPrefix = ExtractKeyPrefix(key);
        activity?.SetTag("cache.key_prefix", keyPrefix);

        var result = await _cache.TryGetAsync<T>(key, token: ct).ConfigureAwait(false);

        if (!result.HasValue)
            RecordMiss(keyPrefix);

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

        using var activity = _activitySource.StartActivity("cache.get_or_set", ActivityKind.Client);
        var keyPrefix = ExtractKeyPrefix(key);
        activity?.SetTag("cache.key_prefix", keyPrefix);

        var entryOptions = BuildEntryOptions(policy, _cache.DefaultEntryOptions);
        var tags = policy.Tags.Count > 0 ? policy.Tags : null;

        // Set from inside the factory: a miss runs it, a hit (L1 or L2) does not.
        var factoryInvoked = false;

        var result = await _cache.GetOrSetAsync<T>(
            key,
            async (fusionContext, token) =>
            {
                factoryInvoked = true;
                Log.FactoryInvoked(_logger, keyPrefix);
                _cacheMisses.Add(1, new KeyValuePair<string, object?>("cache.key_prefix", keyPrefix));
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
                    activity?.SetStatus(ActivityStatusCode.Error, ex.GetType().Name);
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
        var keyPrefix = ExtractKeyPrefix(key);
        activity?.SetTag("cache.key_prefix", keyPrefix);

        var entryOptions = BuildEntryOptions(policy, _cache.DefaultEntryOptions);
        var tags = policy.Tags.Count > 0 ? policy.Tags : null;

        try
        {
            await _cache.SetAsync(key, value, entryOptions, tags, token: ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _cacheErrors.Add(1,
                new KeyValuePair<string, object?>("cache.error_type", ex.GetType().Name));
            activity?.SetStatus(ActivityStatusCode.Error, ex.GetType().Name);
            throw;
        }

        Log.CacheSet(_logger, keyPrefix);
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
                Log.CacheSet(_logger, ExtractKeyPrefix(entry.Key));
            }).ConfigureAwait(false);
    }

    public async ValueTask RemoveAsync(string key, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        await _cache.RemoveAsync(key, token: ct).ConfigureAwait(false);
        Log.CacheRemoved(_logger, ExtractKeyPrefix(key));
    }

    public async ValueTask ExpireAsync(string key, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        await _cache.ExpireAsync(key, token: ct).ConfigureAwait(false);
        Log.CacheExpired(_logger, ExtractKeyPrefix(key));
    }

    public async ValueTask RemoveByTagAsync(string tag, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);

        await _cache.RemoveByTagAsync(tag, token: ct).ConfigureAwait(false);
        Log.CacheTagRemoved(_logger, DescribeTag(tag));
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
            Log.CacheTagRemoved(_logger, DescribeTag(tag));
    }

    public async ValueTask ClearAsync(CancellationToken ct = default)
    {
        await _cache.ClearAsync(allowFailSafe: false, token: ct).ConfigureAwait(false);
        Log.CacheCleared(_logger);
    }

    private void RecordMiss(string keyPrefix)
    {
        Log.CacheMiss(_logger, keyPrefix);
        _cacheMisses.Add(1, new KeyValuePair<string, object?>("cache.key_prefix", keyPrefix));
    }

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

    // Source-generated log methods. Keys are logged as {service}:{entity} prefixes and tenant tags as
    // @tenant:{tag}, so ids and tenant ids never reach logs.
    private static partial class Log
    {
        [LoggerMessage(EventId = LoggingEventIdRanges.Caching + 10, Level = LogLevel.Debug,
            Message = "Cache miss for {KeyPrefix}")]
        internal static partial void CacheMiss(ILogger logger, string keyPrefix);

        [LoggerMessage(EventId = LoggingEventIdRanges.Caching + 11, Level = LogLevel.Debug,
            Message = "Cache set for {KeyPrefix}")]
        internal static partial void CacheSet(ILogger logger, string keyPrefix);

        [LoggerMessage(EventId = LoggingEventIdRanges.Caching + 12, Level = LogLevel.Debug,
            Message = "Cache factory invoked for {KeyPrefix}")]
        internal static partial void FactoryInvoked(ILogger logger, string keyPrefix);

        [LoggerMessage(EventId = LoggingEventIdRanges.Caching + 13, Level = LogLevel.Debug,
            Message = "Cache entry removed for {KeyPrefix}")]
        internal static partial void CacheRemoved(ILogger logger, string keyPrefix);

        [LoggerMessage(EventId = LoggingEventIdRanges.Caching + 14, Level = LogLevel.Debug,
            Message = "Cache entries removed for tag {Tag}")]
        internal static partial void CacheTagRemoved(ILogger logger, string tag);

        [LoggerMessage(EventId = LoggingEventIdRanges.Caching + 16, Level = LogLevel.Debug,
            Message = "Cache entry expired for {KeyPrefix}")]
        internal static partial void CacheExpired(ILogger logger, string keyPrefix);

        [LoggerMessage(EventId = LoggingEventIdRanges.Caching + 17, Level = LogLevel.Warning,
            Message = "Cache cleared: every entry of this cache was removed")]
        internal static partial void CacheCleared(ILogger logger);
    }
}

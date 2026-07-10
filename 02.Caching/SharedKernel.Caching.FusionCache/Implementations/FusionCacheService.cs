using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Primitives.Logging;
using ZiggyCreatures.Caching.Fusion;

namespace SharedKernel.Caching.FusionCache.Implementations;

/// <summary>
/// <see cref="ICacheService"/> implementation backed by FusionCache.
/// Provides L1 in-process memory cache with optional L2 Redis distributed backplane,
/// stampede protection, background refresh, and fail-safe semantics.
/// </summary>
/// <remarks>
/// Emits <see cref="System.Diagnostics.Metrics"/> instruments under the meter
/// <c>SharedKernel.Caching</c> (version <c>1.0</c>). Consumers attach a
/// <see cref="MeterListener"/> or configure an OTel metrics exporter to receive
/// these metrics.
/// </remarks>
internal sealed partial class FusionCacheService : ICacheService
{
    // ---------------------------------------------------------------------------
    // OTel Metrics — static readonly, AOT-safe, shared across all instances.
    // BCL guarantees negligible overhead when no listener is attached.
    // ---------------------------------------------------------------------------

    /// <summary>
    /// The meter for all SharedKernel.Caching metrics.
    /// Consumers attach a <see cref="MeterListener"/> or configure an OTel metrics
    /// exporter to receive these metrics.
    /// </summary>
    private static readonly Meter _meter = new("SharedKernel.Caching", "1.0");

    /// <summary>Counts cache hits (L1 memory hits from FusionCache events).</summary>
    private static readonly Counter<long> _cacheHits =
        _meter.CreateCounter<long>(
            "cache.hits",
            description: "Number of cache hits. Tag cache.key_prefix = {service}:{entity}.");

    /// <summary>Counts cache misses (L1 memory misses from FusionCache events).</summary>
    private static readonly Counter<long> _cacheMisses =
        _meter.CreateCounter<long>(
            "cache.misses",
            description: "Number of cache misses. Tag cache.key_prefix = {service}:{entity}.");

    /// <summary>Records factory execution duration in milliseconds on cache miss.</summary>
    private static readonly Histogram<double> _factoryDuration =
        _meter.CreateHistogram<double>(
            "cache.factory.duration",
            unit: "ms",
            description: "Factory execution duration in milliseconds. Tag cache.key_prefix = {service}:{entity}.");

    /// <summary>Counts errors (factory exceptions or SetAsync exceptions).</summary>
    private static readonly Counter<long> _cacheErrors =
        _meter.CreateCounter<long>(
            "cache.errors",
            description: "Number of cache errors. Tag cache.error_type = exception type name.");

    /// <summary>Counts L1 memory evictions (subscribed via FusionCache Events.Memory.Eviction).</summary>
    private static readonly Counter<long> _cacheEvictions =
        _meter.CreateCounter<long>(
            "cache.evictions",
            description: "Number of L1 memory evictions. Tag cache.eviction_reason = EvictionReason name.");

    // ---------------------------------------------------------------------------

    private readonly IFusionCache _cache;
    private readonly ILogger<FusionCacheService> _logger;

    public FusionCacheService(IFusionCache cache, ILogger<FusionCacheService> logger)
    {
        _cache = cache;
        _logger = logger;

        // Subscribe to FusionCache memory events for hit/miss/eviction.
        // Prefer event subscription over call-site instrumentation to avoid duplication.
        _cache.Events.Memory.Hit += OnMemoryHit;
        _cache.Events.Memory.Miss += OnMemoryMiss;
        _cache.Events.Memory.Eviction += OnMemoryEviction;
    }

    // ---------------------------------------------------------------------------
    // FusionCache event handlers
    // ---------------------------------------------------------------------------

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

    // ---------------------------------------------------------------------------
    // Key prefix extraction — {service}:{entity} from {service}:{entity}:{id}[:...]
    // Never includes the id segment to avoid high-cardinality Prometheus labels.
    // ---------------------------------------------------------------------------

    internal static string ExtractKeyPrefix(string key)
    {
        if (string.IsNullOrEmpty(key))
            return string.Empty;

        var firstColon = key.IndexOf(':', StringComparison.Ordinal);
        if (firstColon < 0)
            return key; // single-segment key — return as-is

        var secondColon = key.IndexOf(':', firstColon + 1);
        return secondColon < 0
            ? key                                   // two segments — return whole key
            : key[..secondColon];                   // three or more — truncate after second segment
    }

    /// <inheritdoc />
    public async ValueTask<T?> GetAsync<T>(string key, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var result = await _cache.TryGetAsync<T>(key, token: ct).ConfigureAwait(false);

        if (!result.HasValue)
        {
            Log.CacheMiss(_logger, key);
            // TryGetAsync does not fire FusionCache's Memory.Miss event, so we
            // instrument the miss path directly here for GetAsync callers.
            _cacheMisses.Add(1,
                new KeyValuePair<string, object?>("cache.key_prefix", ExtractKeyPrefix(key)));
        }

        return result.HasValue ? result.Value : default;
    }

    /// <inheritdoc />
    public async ValueTask SetAsync<T>(string key, T value, CachePolicy policy, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(policy);

        var entryOptions = BuildEntryOptions(policy);
        IEnumerable<string>? tags = policy.Tags.Length > 0 ? policy.Tags : null;

        try
        {
            await _cache.SetAsync(key, value, entryOptions, tags, token: ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _cacheErrors.Add(1,
                new KeyValuePair<string, object?>("cache.error_type", ex.GetType().Name));
            throw;
        }

        Log.CacheSet(_logger, key);
    }

    /// <inheritdoc />
    public async ValueTask<T> GetOrSetAsync<T>(
        string key,
        Func<CancellationToken, ValueTask<T>> factory,
        CachePolicy policy,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(policy);

        var entryOptions = BuildEntryOptions(policy);
        IEnumerable<string>? tags = policy.Tags.Length > 0 ? policy.Tags : null;

        // Adapt ValueTask<T> factory to FusionCache's Task<T> factory via async/await.
        // The state machine allocation occurs only on actual cache misses — not on every call.
        // Stopwatch measures factory elapsed time for the cache.factory.duration histogram.
        var keyPrefix = ExtractKeyPrefix(key);
        var result = await _cache.GetOrSetAsync<T>(
            key,
            async token =>
            {
                Log.FactoryInvoked(_logger, key);
                var sw = Stopwatch.StartNew();
                try
                {
                    var value = await factory(token).ConfigureAwait(false);
                    sw.Stop();
                    _factoryDuration.Record(
                        sw.Elapsed.TotalMilliseconds,
                        new KeyValuePair<string, object?>("cache.key_prefix", keyPrefix));
                    return value;
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    _cacheErrors.Add(1,
                        new KeyValuePair<string, object?>("cache.error_type", ex.GetType().Name));
                    throw;
                }
            },
            MaybeValue<T>.None,
            entryOptions,
            tags,
            token: ct).ConfigureAwait(false);

        return result;
    }


    /// <inheritdoc />
    public async ValueTask<IReadOnlyDictionary<string, T?>> GetManyAsync<T>(
        IEnumerable<string> keys,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(keys);

        var result = new Dictionary<string, T?>();

        foreach (var key in keys)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key);

            var entry = await _cache.TryGetAsync<T>(key, token: ct).ConfigureAwait(false);
            result[key] = entry.HasValue ? entry.Value : default;

            if (!entry.HasValue)
                Log.CacheMiss(_logger, key);
        }

        return result;
    }

    /// <inheritdoc />
    public async ValueTask SetManyAsync<T>(
        IReadOnlyDictionary<string, T> entries,
        CachePolicy policy,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(policy);

        var entryOptions = BuildEntryOptions(policy);
        IEnumerable<string>? tags = policy.Tags.Length > 0 ? policy.Tags : null;

        foreach (var (key, value) in entries)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key);

            await _cache.SetAsync(key, value, entryOptions, tags, token: ct).ConfigureAwait(false);
            Log.CacheSet(_logger, key);
        }
    }

    /// <inheritdoc />
    public async ValueTask RemoveAsync(string key, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        await _cache.RemoveAsync(key, token: ct).ConfigureAwait(false);
        Log.CacheRemoved(_logger, key);
    }

    /// <inheritdoc />
    public async ValueTask RemoveByTagAsync(string tag, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);

        await _cache.RemoveByTagAsync(tag, token: ct).ConfigureAwait(false);
        Log.CacheTagRemoved(_logger, tag);
    }

    // Converts a CachePolicy to FusionCache entry options.
    // Throws InvalidOperationException when NeverExpire and SlidingWindow are combined.
    private static FusionCacheEntryOptions BuildEntryOptions(CachePolicy policy)
    {
        // Guard: NeverExpire + SlidingWindow is a logical contradiction — an entry that
        // never expires on an absolute TTL should not also have an idle expiration window.
        if (policy.L1Duration == TimeSpan.MaxValue && policy.SlidingWindow.HasValue)
            throw new InvalidOperationException(
                "CachePolicy.Sliding is incompatible with CachePolicy.NeverExpire.");

        var options = new FusionCacheEntryOptions(policy.L1Duration)
        {
            IsFailSafeEnabled = policy.FailSafeEnabled,
            // Size = 1 so every entry counts as one unit against the MemoryCache SizeLimit
            // set via CachingOptions.L1SizeLimit (entry count — not bytes).
            Size = 1,
        };

        // Sliding expiration approximation for L1.
        //
        // FusionCache 2.6.0 does not expose a native sliding-expiration property on
        // FusionCacheEntryOptions. We approximate idle-TTL behaviour by setting the
        // L1 MemoryCacheDuration to SlidingWindow and enabling an aggressive
        // EagerRefreshThreshold (0.9). Each access near the end of the SlidingWindow
        // triggers a background re-validation that effectively resets the L1 TTL as
        // long as the entry continues to be accessed.
        //
        // The absolute ceiling is the L1Duration set on the policy (constructor arg above).
        // For CachePolicy.Sliding() this is CachePolicy.Default's 5 min.
        // L2 Redis does not support sliding expiry — L2 entries expire at the
        // absolute L2Duration ceiling. This L1-only limitation is documented on
        // CachePolicy.SlidingWindow and CachePolicy.Sliding.
        if (policy.SlidingWindow.HasValue)
            options.SetMemoryCacheDuration(policy.SlidingWindow.Value);

        // L2 distributed cache duration — sets the distributed cache TTL
        options.SetDistributedCacheDuration(policy.L2Duration);

        // Eager refresh: trigger background refresh at the configured fraction of TTL.
        // When SlidingWindow is set, use 0.9 as the threshold for the approximation
        // (access near end of SlidingWindow triggers background refresh, resetting TTL).
        if (policy.SlidingWindow.HasValue)
            options.EagerRefreshThreshold = 0.9f;
        else if (policy.EagerRefreshThreshold.HasValue)
            options.EagerRefreshThreshold = (float)policy.EagerRefreshThreshold.Value;

        return options;
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
    }
}

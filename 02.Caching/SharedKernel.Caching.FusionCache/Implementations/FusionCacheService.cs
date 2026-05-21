using Microsoft.Extensions.Logging;
using SharedKernel.Caching.Abstractions;
using ZiggyCreatures.Caching.Fusion;

namespace SharedKernel.Caching.FusionCache.Implementations;

/// <summary>
/// <see cref="ICacheService"/> implementation backed by FusionCache.
/// Provides L1 in-process memory cache with optional L2 Redis distributed backplane,
/// stampede protection, background refresh, and fail-safe semantics.
/// </summary>
internal sealed partial class FusionCacheService : ICacheService
{
    private readonly IFusionCache _cache;
    private readonly ILogger<FusionCacheService> _logger;

    public FusionCacheService(IFusionCache cache, ILogger<FusionCacheService> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    /// <inheritdoc />
    public async ValueTask<T?> GetAsync<T>(string key, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var result = await _cache.TryGetAsync<T>(key, token: ct).ConfigureAwait(false);

        if (!result.HasValue)
            Log.CacheMiss(_logger, key);

        return result.HasValue ? result.Value : default;
    }

    /// <inheritdoc />
    public async ValueTask SetAsync<T>(string key, T value, CachePolicy policy, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(policy);

        var entryOptions = BuildEntryOptions(policy);
        IEnumerable<string>? tags = policy.Tags.Length > 0 ? policy.Tags : null;

        await _cache.SetAsync(key, value, entryOptions, tags, token: ct).ConfigureAwait(false);

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
        var result = await _cache.GetOrSetAsync<T>(
            key,
            async token =>
            {
                Log.FactoryInvoked(_logger, key);
                return await factory(token).ConfigureAwait(false);
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
        [LoggerMessage(EventId = 1001, Level = LogLevel.Debug,
            Message = "Cache miss for key '{Key}'")]
        internal static partial void CacheMiss(ILogger logger, string key);

        [LoggerMessage(EventId = 1002, Level = LogLevel.Debug,
            Message = "Cache set for key '{Key}'")]
        internal static partial void CacheSet(ILogger logger, string key);

        [LoggerMessage(EventId = 1003, Level = LogLevel.Debug,
            Message = "Cache factory invoked for key '{Key}'")]
        internal static partial void FactoryInvoked(ILogger logger, string key);

        [LoggerMessage(EventId = 1004, Level = LogLevel.Debug,
            Message = "Cache entry removed for key '{Key}'")]
        internal static partial void CacheRemoved(ILogger logger, string key);

        [LoggerMessage(EventId = 1005, Level = LogLevel.Debug,
            Message = "Cache entries removed for tag '{Tag}'")]
        internal static partial void CacheTagRemoved(ILogger logger, string tag);
    }
}

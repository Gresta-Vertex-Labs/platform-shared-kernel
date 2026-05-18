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
        Func<CancellationToken, Task<T>> factory,
        CachePolicy policy,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(policy);

        var entryOptions = BuildEntryOptions(policy);
        IEnumerable<string>? tags = policy.Tags.Length > 0 ? policy.Tags : null;

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
    private static FusionCacheEntryOptions BuildEntryOptions(CachePolicy policy)
    {
        var options = new FusionCacheEntryOptions(policy.L1Duration)
        {
            IsFailSafeEnabled = policy.FailSafeEnabled,
        };

        // L2 distributed cache duration — sets the distributed cache TTL
        options.SetDistributedCacheDuration(policy.L2Duration);

        // Eager refresh: trigger background refresh at the configured fraction of TTL
        if (policy.EagerRefreshThreshold.HasValue)
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

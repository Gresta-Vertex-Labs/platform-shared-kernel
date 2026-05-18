namespace SharedKernel.Caching.Abstractions;

/// <summary>
/// Provides a unified hybrid cache surface backed by FusionCache.
/// Supports in-process L1 memory cache and optional distributed L2 Redis backplane
/// with built-in stampede protection, background refresh, and fail-safe.
/// </summary>
/// <remarks>
/// <para>
/// Callers <b>must</b> prefer <see cref="GetOrSetAsync{T}"/> over separate
/// <see cref="GetAsync{T}"/> + <see cref="SetAsync{T}"/> calls. The sequential
/// get-then-set pattern is a cache-stampede bug — it provides no stampede protection.
/// </para>
/// <para>
/// Cache keys are prefix-namespaced by the consuming service, not by this package.
/// Convention: <c>"{service}:{entity}:{id}"</c>. Use <see cref="ICacheKeyProvider"/>
/// to construct keys rather than building strings inline.
/// </para>
/// </remarks>
public interface ICacheService
{
    /// <summary>
    /// Attempts to retrieve the cached value for <paramref name="key"/>.
    /// Returns <see langword="null"/> when the key is not present in either L1 or L2.
    /// </summary>
    /// <typeparam name="T">The type of the cached value.</typeparam>
    /// <param name="key">The non-empty cache key.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The cached value, or <see langword="null"/> if not found.</returns>
    ValueTask<T?> GetAsync<T>(string key, CancellationToken ct = default);

    /// <summary>
    /// Stores <paramref name="value"/> in the cache under <paramref name="key"/>
    /// using the supplied <paramref name="policy"/>.
    /// </summary>
    /// <typeparam name="T">The type of the value to cache.</typeparam>
    /// <param name="key">The non-empty cache key.</param>
    /// <param name="value">The value to store.</param>
    /// <param name="policy">Cache policy controlling TTL, tags, and refresh behaviour.</param>
    /// <param name="ct">Cancellation token.</param>
    ValueTask SetAsync<T>(string key, T value, CachePolicy policy, CancellationToken ct = default);

    /// <summary>
    /// Returns the cached value for <paramref name="key"/> if present; otherwise invokes
    /// <paramref name="factory"/>, caches the result using <paramref name="policy"/>, and returns it.
    /// </summary>
    /// <remarks>
    /// This is the preferred method for all cache reads. FusionCache ensures the factory is
    /// called exactly once even under concurrent requests for the same key (stampede protection).
    /// </remarks>
    /// <typeparam name="T">The type of the cached value.</typeparam>
    /// <param name="key">The non-empty cache key.</param>
    /// <param name="factory">Async delegate invoked on cache miss. Must not be <see langword="null"/>.</param>
    /// <param name="policy">Cache policy controlling TTL, tags, and refresh behaviour.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The cached or freshly-computed value.</returns>
    ValueTask<T> GetOrSetAsync<T>(
        string key,
        Func<CancellationToken, Task<T>> factory,
        CachePolicy policy,
        CancellationToken ct = default);

    /// <summary>
    /// Removes the entry for <paramref name="key"/> from all cache layers.
    /// No-ops silently if the key does not exist.
    /// </summary>
    /// <param name="key">The non-empty cache key.</param>
    /// <param name="ct">Cancellation token.</param>
    ValueTask RemoveAsync(string key, CancellationToken ct = default);

    /// <summary>
    /// Removes all cache entries that carry the specified <paramref name="tag"/>.
    /// </summary>
    /// <remarks>
    /// Tag-based eviction is the recommended pattern for invalidating groups of related entries
    /// (e.g., all entries belonging to a tenant or entity type).
    /// </remarks>
    /// <param name="tag">The tag whose entries should be evicted.</param>
    /// <param name="ct">Cancellation token.</param>
    ValueTask RemoveByTagAsync(string tag, CancellationToken ct = default);
}

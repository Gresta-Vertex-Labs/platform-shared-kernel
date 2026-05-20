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
    /// <para>
    /// This is the preferred method for all cache reads. FusionCache ensures the factory is
    /// called exactly once even under concurrent requests for the same key (stampede protection).
    /// </para>
    /// <para>
    /// <b>Negative-result caching:</b> To cache the <em>absence</em> of an entity (preventing
    /// repeated expensive lookups for missing records), call this method with <c>T = string?</c>
    /// (or any nullable type). When the factory returns <see langword="null"/>, that null result
    /// is stored as a genuine cache entry — subsequent calls return <see langword="null"/> directly
    /// without invoking the factory again. Do not use <see cref="CachePolicy.NeverExpire"/> for
    /// this pattern; use a bounded TTL so stale absences eventually expire.
    /// Example: <c>await cache.GetOrSetAsync&lt;MyEntity?&gt;(key, async ct =&gt; await db.FindAsync(id, ct), policy)</c>
    /// </para>
    /// <para>
    /// <b>Migration note (breaking change):</b> The factory delegate was changed from
    /// <c>Func&lt;CancellationToken, Task&lt;T&gt;&gt;</c> to <c>Func&lt;CancellationToken, ValueTask&lt;T&gt;&gt;</c>
    /// to align with .NET 10 async conventions and eliminate per-call <c>.AsTask()</c> allocations.
    /// If you have an existing <c>Task&lt;T&gt;</c> factory, wrap it:
    /// <c>async ct =&gt; await existingFactory(ct)</c>.
    /// </para>
    /// </remarks>
    /// <typeparam name="T">
    /// The type of the cached value. Use a nullable type (<c>T?</c>) to enable negative-result
    /// caching (caching the absence of an entity as a genuine cache hit).
    /// </typeparam>
    /// <param name="key">The non-empty cache key.</param>
    /// <param name="factory">
    /// Async delegate invoked on cache miss. Must not be <see langword="null"/>.
    /// The delegate receives the ambient <see cref="CancellationToken"/> and returns a
    /// <see cref="ValueTask{T}"/>. May return <see langword="null"/> when <typeparamref name="T"/>
    /// is nullable — the null result will be cached as a genuine entry.
    /// </param>
    /// <param name="policy">Cache policy controlling TTL, tags, and refresh behaviour.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// The cached or freshly-computed value; <see langword="null"/> when <typeparamref name="T"/>
    /// is nullable and the factory returned <see langword="null"/>.
    /// </returns>
    ValueTask<T> GetOrSetAsync<T>(
        string key,
        Func<CancellationToken, ValueTask<T>> factory,
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

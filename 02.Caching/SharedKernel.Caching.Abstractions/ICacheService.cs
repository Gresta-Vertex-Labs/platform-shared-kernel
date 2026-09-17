namespace SharedKernel.Caching.Abstractions;

/// <summary>
/// A hybrid cache: an in-process memory layer (L1) in front of an optional distributed layer (L2),
/// with stampede protection, fail-safe and eager refresh. The main entry point of this package.
/// </summary>
/// <remarks>
/// <para>
/// <b>Which member.</b> Read through <see cref="GetOrSetAsync{T}(string, Func{CacheFactoryContext, CancellationToken, ValueTask{T}}, CachePolicy, CancellationToken)"/>:
/// it computes a missing value once per key however many callers miss at the same time. Use
/// <see cref="TryGetAsync{T}"/> only to read without computing, and <see cref="SetAsync{T}"/> only
/// to store a value you already have. A <c>TryGetAsync</c> followed by <c>SetAsync</c> is a cache
/// stampede waiting to happen.
/// </para>
/// <para>
/// <b>Keys.</b> Build keys with <see cref="ICacheKeyProvider"/>, which prefixes the service name
/// and escapes each part (<see cref="CacheKeyFormat"/>). For data that belongs to a tenant use
/// <see cref="ITenantCacheService"/> instead of this interface.
/// </para>
/// <para>
/// <b>Distribution.</b> When a distributed layer and backplane are configured, entries are shared by
/// every instance of the service, and <see cref="RemoveAsync"/>, <see cref="ExpireAsync"/>, tag
/// removal and <see cref="ClearAsync"/> reach every instance. Without one, every call affects this
/// process only.
/// </para>
/// <para>
/// <b>Implementations</b> are thread-safe singletons. The platform implementation is
/// <c>SharedKernel.Caching.FusionCache</c> (<c>AddSharedKernelCaching</c>), with
/// <c>SharedKernel.Caching.Redis</c> (<c>AddRedisL2</c>) for the distributed layer.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public sealed class ProductReader(ICacheService cache, ICacheKeyProvider keys, IProductRepository products)
/// {
///     public ValueTask&lt;Product?&gt; GetAsync(Guid id, CancellationToken ct) =&gt;
///         cache.GetOrSetAsync(
///             keys.BuildKey("product", id.ToString("D")),
///             token =&gt; products.FindAsync(id, token),
///             CachePolicy.Default.WithTags("products"),
///             ct);
/// }
/// </code>
/// </example>
public interface ICacheService
{
    /// <summary>Reads the entry for <paramref name="key"/> without computing it.</summary>
    /// <remarks>
    /// A cached <see langword="null"/> or <see langword="default"/> is a hit, so a cached "not found"
    /// is distinguishable from a key that was never cached. Checks L1, then L2.
    /// </remarks>
    /// <typeparam name="T">The type the value was stored as.</typeparam>
    /// <param name="key">The cache key. Must not be null or whitespace.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A hit carrying the value, or <see cref="CacheLookup{T}.Miss"/>.</returns>
    /// <exception cref="ArgumentException"><paramref name="key"/> is null or whitespace.</exception>
    /// <example>
    /// <code>
    /// CacheLookup&lt;int&gt; lookup = await cache.TryGetAsync&lt;int&gt;(key, ct);
    /// if (lookup.TryGetValue(out int count)) { /* hit, even when count is 0 */ }
    /// </code>
    /// </example>
    ValueTask<CacheLookup<T>> TryGetAsync<T>(string key, CancellationToken ct = default);

    /// <summary>Reads several entries without computing them.</summary>
    /// <remarks>Duplicate keys are read once. Each lookup is independent; there is no atomic snapshot.</remarks>
    /// <typeparam name="T">The type the values were stored as.</typeparam>
    /// <param name="keys">The cache keys. Must not be <see langword="null"/>; no key may be null or whitespace.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>One lookup per distinct key, hit or miss.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="keys"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A key is null or whitespace.</exception>
    ValueTask<IReadOnlyDictionary<string, CacheLookup<T>>> TryGetManyAsync<T>(
        IEnumerable<string> keys,
        CancellationToken ct = default);

    /// <summary>
    /// Returns the entry for <paramref name="key"/>, or computes it with <paramref name="factory"/>,
    /// stores it with <paramref name="policy"/> and returns it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The factory runs at most once per key at a time; concurrent callers wait for its result. Use a
    /// nullable <typeparamref name="T"/> to cache "not found".
    /// </para>
    /// <para>
    /// If the factory throws, nothing is stored and the exception reaches the caller, unless fail-safe
    /// serves the previous value. To decide per result whether to store it, use the overload whose
    /// factory receives a <see cref="CacheFactoryContext"/>.
    /// </para>
    /// </remarks>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <param name="key">The cache key. Must not be null or whitespace.</param>
    /// <param name="factory">Computes the value on a miss.</param>
    /// <param name="policy">How the computed value is stored.</param>
    /// <param name="ct">Cancellation token, also passed to the factory.</param>
    /// <returns>The cached or freshly computed value.</returns>
    /// <exception cref="ArgumentException"><paramref name="key"/> is null or whitespace.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="factory"/> or <paramref name="policy"/> is <see langword="null"/>.</exception>
    ValueTask<T> GetOrSetAsync<T>(
        string key,
        Func<CancellationToken, ValueTask<T>> factory,
        CachePolicy policy,
        CancellationToken ct = default);

    /// <summary>
    /// Returns the entry for <paramref name="key"/>, or computes it with <paramref name="factory"/>,
    /// which decides through its <see cref="CacheFactoryContext"/> whether and how long the value is stored.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The computed value always reaches the caller and every concurrent caller waiting on the key. The
    /// context controls only the write: <see cref="CacheFactoryContext.SkipCaching"/> stores nothing,
    /// <see cref="CacheFactoryContext.SetDurations"/> replaces the policy's durations.
    /// </para>
    /// <para>
    /// Typical use: skip caching a failed <c>Result</c>, or cache settled data longer than live data.
    /// </para>
    /// </remarks>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <param name="key">The cache key. Must not be null or whitespace.</param>
    /// <param name="factory">Computes the value on a miss and records its caching decision.</param>
    /// <param name="policy">How the value is stored unless the factory overrides it.</param>
    /// <param name="ct">Cancellation token, also passed to the factory.</param>
    /// <returns>The cached or freshly computed value.</returns>
    /// <exception cref="ArgumentException"><paramref name="key"/> is null or whitespace.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="factory"/> or <paramref name="policy"/> is <see langword="null"/>.</exception>
    /// <example>
    /// <code>
    /// Result&lt;Order&gt; order = await cache.GetOrSetAsync(
    ///     key,
    ///     async (context, token) =&gt;
    ///     {
    ///         Result&lt;Order&gt; loaded = await orders.LoadAsync(id, token);
    ///         if (loaded.IsFailure)
    ///             context.SkipCaching();
    ///         return loaded;
    ///     },
    ///     CachePolicy.For(TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(10)),
    ///     ct);
    /// </code>
    /// </example>
    ValueTask<T> GetOrSetAsync<T>(
        string key,
        Func<CacheFactoryContext, CancellationToken, ValueTask<T>> factory,
        CachePolicy policy,
        CancellationToken ct = default);

    /// <summary>Stores <paramref name="value"/> under <paramref name="key"/>, replacing any existing entry.</summary>
    /// <remarks>Prefer <c>GetOrSetAsync</c> when the value is computed on demand; use this to push a value you already have, such as the result of a write.</remarks>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <param name="key">The cache key. Must not be null or whitespace.</param>
    /// <param name="value">The value to store; may be <see langword="null"/>.</param>
    /// <param name="policy">How the value is stored.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="ArgumentException"><paramref name="key"/> is null or whitespace.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="policy"/> is <see langword="null"/>.</exception>
    ValueTask SetAsync<T>(string key, T value, CachePolicy policy, CancellationToken ct = default);

    /// <summary>Stores several entries with the same policy.</summary>
    /// <remarks>Not atomic: a failure can leave some entries written. Use separate <see cref="SetAsync{T}"/> calls when entries need different policies.</remarks>
    /// <typeparam name="T">The type of the values.</typeparam>
    /// <param name="entries">The entries. Must not be <see langword="null"/>; no key may be null or whitespace.</param>
    /// <param name="policy">How every value is stored.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="ArgumentNullException"><paramref name="entries"/> or <paramref name="policy"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A key is null or whitespace.</exception>
    ValueTask SetManyAsync<T>(IReadOnlyDictionary<string, T> entries, CachePolicy policy, CancellationToken ct = default);

    /// <summary>Removes the entry for <paramref name="key"/> from every layer. Does nothing when it is absent.</summary>
    /// <remarks>
    /// The next read recomputes, and fail-safe has nothing to fall back to. When the source of truth
    /// may be briefly unavailable, prefer <see cref="ExpireAsync"/>.
    /// </remarks>
    /// <param name="key">The cache key. Must not be null or whitespace.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="ArgumentException"><paramref name="key"/> is null or whitespace.</exception>
    ValueTask RemoveAsync(string key, CancellationToken ct = default);

    /// <summary>
    /// Marks the entry for <paramref name="key"/> as expired. Does nothing when it is absent.
    /// </summary>
    /// <remarks>
    /// The next <c>GetOrSetAsync</c> recomputes the value, but if the factory fails, fail-safe can
    /// still serve the expired one.
    /// </remarks>
    /// <param name="key">The cache key. Must not be null or whitespace.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="ArgumentException"><paramref name="key"/> is null or whitespace.</exception>
    ValueTask ExpireAsync(string key, CancellationToken ct = default);

    /// <summary>Removes every entry that carries <paramref name="tag"/>.</summary>
    /// <remarks>Tags are attached with <see cref="CachePolicy.WithTags"/>. For a tenant's tag use <see cref="ITenantCacheService.RemoveByTagAsync"/>.</remarks>
    /// <param name="tag">The tag. Must not be null or whitespace.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="ArgumentException"><paramref name="tag"/> is null or whitespace.</exception>
    ValueTask RemoveByTagAsync(string tag, CancellationToken ct = default);

    /// <summary>Removes every entry that carries any of <paramref name="tags"/>.</summary>
    /// <remarks>An empty collection does nothing.</remarks>
    /// <param name="tags">The tags. Must not be <see langword="null"/>; no tag may be null or whitespace.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="ArgumentNullException"><paramref name="tags"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A tag is null or whitespace.</exception>
    ValueTask RemoveByTagsAsync(IEnumerable<string> tags, CancellationToken ct = default);

    /// <summary>
    /// Removes every entry of this cache: all keys, all tenants, on every instance of the service.
    /// </summary>
    /// <remarks>
    /// A break-glass operation, for example after a data repair. Fail-safe cannot bring cleared values
    /// back, so every key recomputes on its next read. To drop one tenant's entries use
    /// <see cref="ITenantCacheService.RemoveTenantAsync"/>; to drop a group use tags.
    /// </remarks>
    /// <param name="ct">Cancellation token.</param>
    ValueTask ClearAsync(CancellationToken ct = default);
}

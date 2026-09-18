using SharedKernel.Application.Behaviors.Caching;

namespace SharedKernel.Application.Behaviors.CacheInvalidation;

/// <summary>
/// Names one cache entry to evict: the query type that owns the entry, and the key within it.
/// </summary>
/// <remarks>
/// <para>
/// A command cannot name a whole cache key, because a query's key is namespaced by its own type —
/// that namespace is what stops two query types with the same key string reading each other's
/// entries. So the command names the query instead, through
/// <see cref="For{TQuery}(string)"/>:
/// </para>
/// <code>
/// public IReadOnlyCollection&lt;CacheKeyRef&gt; CacheKeysToInvalidate =&gt;
///     [CacheKeyRef.For&lt;GetOrderQuery&gt;(OrderId.ToString())];
/// </code>
/// <para>
/// Naming the query type rather than repeating its key format means renaming or moving the query is
/// a compile error at the command, not a silently-missed eviction discovered in production.
/// </para>
/// </remarks>
public readonly record struct CacheKeyRef
{
    internal CacheKeyRef(Type queryType, string key)
    {
        QueryType = queryType;
        Key = key;
    }

    /// <summary>Gets the query type whose entry this reference names.</summary>
    public Type QueryType { get; }

    /// <summary>Gets the key within that query's namespace, matching its <see cref="ICacheableQuery.CacheKey"/>.</summary>
    public string Key { get; }

    /// <summary>Names the entry <typeparamref name="TQuery"/> wrote under <paramref name="key"/>.</summary>
    /// <typeparam name="TQuery">The query type that owns the entry.</typeparam>
    /// <param name="key">The value that query's <see cref="ICacheableQuery.CacheKey"/> produces for this entry.</param>
    /// <returns>The reference.</returns>
    /// <exception cref="ArgumentException"><paramref name="key"/> is null or whitespace.</exception>
    public static CacheKeyRef For<TQuery>(string key)
        where TQuery : ICacheableQuery
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return new CacheKeyRef(typeof(TQuery), key);
    }
}

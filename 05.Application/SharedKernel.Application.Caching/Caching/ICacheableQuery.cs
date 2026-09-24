using MediatR;
using SharedKernel.Caching.Abstractions;

namespace SharedKernel.Application.Caching;

/// <summary>
/// The key, scope, and policy of a cacheable query. Implement <see cref="ICacheableQuery{TValue}"/>;
/// this base cannot be implemented on its own.
/// </summary>
/// <remarks>
/// The caching behavior is constrained to this non-generic base because a pipeline behavior only
/// knows the query's response type, <c>Result&lt;TValue&gt;</c>, not <c>TValue</c>. The internal
/// member is implemented by <see cref="ICacheableQuery{TValue}"/>, where <c>TValue</c> is known, so
/// the cached value is typed without reflection.
/// </remarks>
public interface ICacheableQuery
{
    /// <summary>Gets the cache policy controlling durations, tags, and fail-safe for this query.</summary>
    CachePolicy CachePolicy { get; }

    /// <summary>
    /// Gets this query instance's identity within its own namespace. Build it from every parameter
    /// that changes the result, for example <c>$"{OrderId}"</c> or <c>$"{OrderId}:{Currency}"</c>.
    /// </summary>
    /// <remarks>
    /// This is the identifier, not the whole key. The query type supplies the namespace and the
    /// registered <c>ICacheKeyProvider</c> supplies the service name and the escaping, so this value
    /// only has to distinguish one instance of <i>this</i> query from another.
    /// </remarks>
    string CacheKey { get; }

    /// <summary>
    /// Gets the identity this query's entries are partitioned by. Defaults to
    /// <see cref="CacheScope.Tenant"/>.
    /// </summary>
    /// <remarks>
    /// Declare <see cref="CacheScope.User"/> when the value depends on who is asking, and
    /// <see cref="CacheScope.Global"/> only when it depends on neither tenant nor caller. A command
    /// invalidating this query must declare the same scope.
    /// </remarks>
    CacheScope Scope => CacheScope.Tenant;

    /// <summary>
    /// Gets a value indicating whether this execution ignores any existing entry, runs the handler,
    /// and writes its value over the entry. Defaults to <see langword="false"/>.
    /// </summary>
    /// <remarks>
    /// The force-refresh path for a caller that knows the entry is stale — a "reload" action, or a
    /// read that must observe a write made outside this service. It still writes, so the next
    /// ordinary caller is served from the refreshed entry; it is not a way to opt one call out of
    /// caching. Stampede protection does not apply to a refresh, because the point is to bypass the
    /// existing entry.
    /// </remarks>
    bool RefreshCache => false;

    internal ValueTask<TResponse> ExecuteAsync<TResponse>(
        ICacheService cache,
        string key,
        CachePolicy policy,
        CacheExecution execution,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken);
}

/// <summary>
/// A query whose successful value is cached by this package's caching behavior.
/// </summary>
/// <typeparam name="TValue">
/// The value the query returns inside <c>Result&lt;TValue&gt;</c>, and the type stored in the cache.
/// </typeparam>
/// <remarks>
/// <para>
/// This interface is also an <see cref="IQuery{TResponse}"/>, so a query declares it alone:
/// <c>record GetOrderQuery(Guid OrderId) : ICacheableQuery&lt;OrderDto&gt;</c>.
/// </para>
/// <para>
/// Only <typeparamref name="TValue"/> is cached, never the <c>Result</c>. It is written to the
/// distributed cache with the cache's JSON serializer, so it must round-trip through
/// <c>System.Text.Json</c>; when the service registers a <c>SerializerContext</c>, add
/// <typeparamref name="TValue"/> to it. Failures are never cached.
/// </para>
/// <para>
/// The query supplies its own key because only it holds the parameters that distinguish one result
/// from another. Never implemented by a command.
/// </para>
/// </remarks>
public interface ICacheableQuery<TValue> : ICacheableQuery, IQuery<TValue>
{
    /// <summary>
    /// Decides whether a successful value is worth caching. Defaults to caching every success.
    /// </summary>
    /// <param name="value">The value the handler returned.</param>
    /// <returns><see langword="true"/> to cache it; <see langword="false"/> to return it uncached.</returns>
    /// <remarks>
    /// Use it to keep a negative or empty result out of the cache when the caller is likely to write
    /// the missing data immediately — an empty collection, a zero count, a partially-populated
    /// projection. Returning <see langword="false"/> leaves any existing entry untouched. Must be
    /// pure and must not throw: it runs inside the cache factory, where a throw propagates to every
    /// waiting caller.
    /// </remarks>
    bool ShouldCache(TValue value) => true;

    ValueTask<TResponse> ICacheableQuery.ExecuteAsync<TResponse>(
        ICacheService cache,
        string key,
        CachePolicy policy,
        CacheExecution execution,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken) =>
        CachedQueryValue<TValue>.ExecuteAsync(
            cache, key, policy, RefreshCache, ShouldCache, execution, next, cancellationToken);
}

using MediatR;
using SharedKernel.Application.Messaging;
using SharedKernel.Caching.Abstractions;

namespace SharedKernel.Application.Behaviors.Caching;

/// <summary>
/// The key and policy of a cacheable query. Implement <see cref="ICacheableQuery{TValue}"/>; this
/// base cannot be implemented on its own.
/// </summary>
/// <remarks>
/// <see cref="CachingBehavior{TRequest,TResponse}"/> is constrained to this non-generic base because a
/// pipeline behavior only knows the query's response type, <c>Result&lt;TValue&gt;</c>, not
/// <c>TValue</c>. The internal member is implemented by <see cref="ICacheableQuery{TValue}"/>, where
/// <c>TValue</c> is known, so the cached value is typed without reflection.
/// </remarks>
public interface ICacheableQuery
{
    /// <summary>Gets the cache policy controlling durations, tags, and fail-safe for this query.</summary>
    CachePolicy CachePolicy { get; }

    /// <summary>
    /// Gets the cache key for this query instance. Build it from every parameter that changes the
    /// result, for example <c>$"order:{OrderId}"</c>.
    /// </summary>
    string CacheKey { get; }

    internal ValueTask<TResponse> GetOrSetAsync<TResponse>(
        ICacheService cache,
        string key,
        CachePolicy policy,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken);
}

/// <summary>
/// A query whose successful value is cached by <see cref="CachingBehavior{TRequest,TResponse}"/>.
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
    ValueTask<TResponse> ICacheableQuery.GetOrSetAsync<TResponse>(
        ICacheService cache,
        string key,
        CachePolicy policy,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken) =>
        CachedQueryValue<TValue>.GetOrSetAsync(cache, key, policy, next, cancellationToken);
}

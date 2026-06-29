using MediatR;
using SharedKernel.Caching.Abstractions;

namespace SharedKernel.Application.Behaviors.Caching;

/// <summary>
/// Marks a query as eligible for automatic cache wrapping by <see cref="CachingBehavior{TRequest,TResponse}"/>.
/// </summary>
/// <typeparam name="TResponse">The response type returned by the query.</typeparam>
/// <remarks>
/// The query instance supplies its own pre-computed <see cref="CacheKey"/> because it alone holds
/// the discriminating parameters (e.g. an entity ID). <c>ICacheKeyProvider</c>
/// (<c>SharedKernel.Caching.Abstractions</c>) is available as a DI service for queries that want
/// the platform key format, but calling it is the query's responsibility, not the behavior's —
/// this keeps the behavior itself simple. Never implemented by a command type; typically declared
/// as <c>ICacheableQuery&lt;Result&lt;TPayload&gt;&gt;</c> so it resolves to the exact same
/// <see cref="IRequest{TResponse}"/> contract as <c>IQuery&lt;TPayload&gt;</c>.
/// </remarks>
public interface ICacheableQuery<TResponse> : IRequest<TResponse>
{
    /// <summary>Gets the cache policy controlling TTL, tags, and refresh behaviour for this query.</summary>
    CachePolicy CachePolicy { get; }

    /// <summary>Gets the pre-computed cache key for this query instance.</summary>
    string CacheKey { get; }
}

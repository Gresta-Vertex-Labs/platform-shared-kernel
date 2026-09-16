using SharedKernel.Caching.Abstractions;

namespace SharedKernel.Application.Behaviors.Caching;

/// <summary>
/// Marks a query as eligible for automatic cache wrapping by <see cref="CachingBehavior{TRequest,TResponse}"/>.
/// </summary>
/// <typeparam name="TResponse">The response type returned by the query.</typeparam>
/// <remarks>
/// A plain interface — deliberately does not itself extend <c>IRequest{TResponse}</c>; a query
/// implements both <c>SharedKernel.Application.Messaging.IQuery{TResponse}</c> (or MediatR's
/// <c>IRequest{TResponse}</c> directly) and this interface side by side. The query instance
/// supplies its own pre-computed <see cref="CacheKey"/> because it alone holds the discriminating
/// parameters (e.g. an entity ID); <see cref="CachingBehavior{TRequest,TResponse}"/> never derives
/// one itself. Never implemented by a command type.
/// </remarks>
public interface ICacheableQuery<TResponse>
{
    /// <summary>Gets the cache policy controlling TTL, tags, and refresh behaviour for this query.</summary>
    CachePolicy CachePolicy { get; }

    /// <summary>Gets the pre-computed cache key for this query instance.</summary>
    string CacheKey { get; }
}

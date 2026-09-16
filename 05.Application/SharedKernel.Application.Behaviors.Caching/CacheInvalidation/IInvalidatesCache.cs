using SharedKernel.Application.Behaviors.Caching;

namespace SharedKernel.Application.Behaviors.CacheInvalidation;

/// <summary>
/// Marks a command as declaring the cache key(s)/tag(s) it renders stale on success.
/// </summary>
/// <remarks>
/// Mirrors <see cref="ICacheableQuery{TResponse}"/>'s exact self-supplied pattern — the command
/// instance alone holds the discriminating parameters (e.g. the entity ID it just mutated), so it
/// computes and supplies its own key list; <see cref="CacheInvalidationBehavior{TRequest,TResponse}"/>
/// never derives keys itself. Tag-based eviction (<see cref="CacheTagsToInvalidate"/>) is an
/// optional second property for commands that invalidate a whole tag rather than enumerating
/// individual keys — both are supported, neither is required if the other is supplied. Never
/// implemented by a query type — this is the write-side half of the read/write caching pair.
/// </remarks>
public interface IInvalidatesCache
{
    /// <summary>Gets the cache key(s) to remove on success.</summary>
    IReadOnlyCollection<string> CacheKeysToInvalidate { get; }

    /// <summary>Gets the cache tag(s) to remove on success.</summary>
    IReadOnlyCollection<string> CacheTagsToInvalidate => [];
}

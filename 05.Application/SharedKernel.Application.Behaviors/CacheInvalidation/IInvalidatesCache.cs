namespace SharedKernel.Application.Behaviors.CacheInvalidation;

/// <summary>
/// Marks a command as declaring the cache key(s)/tag(s) it renders stale on success.
/// </summary>
/// <remarks>
/// Mirrors <c>ICacheableQuery&lt;TResponse&gt;.CacheKey</c>'s exact self-supplied pattern — the
/// command instance alone holds the discriminating parameters (e.g. the entity ID it just
/// mutated), so it computes and supplies its own key list; the behavior never derives keys itself.
/// Tag-based eviction (<see cref="CacheTagsToInvalidate"/>, calling
/// <c>ICacheService.RemoveByTagAsync</c>) is available as an optional second property for commands
/// that invalidate a whole tag rather than enumerable individual keys — both are supported,
/// neither is required if the other is supplied. Never implemented by a query type — this is the
/// write-side half of the read/write caching pair; <c>ICacheableQuery&lt;TResponse&gt;</c> remains
/// the read-side half.
/// </remarks>
public interface IInvalidatesCache
{
    /// <summary>Gets the cache key(s) to remove via <c>ICacheService.RemoveAsync</c> on success.</summary>
    IReadOnlyCollection<string> CacheKeysToInvalidate { get; }

    /// <summary>Gets the cache tag(s) to remove via <c>ICacheService.RemoveByTagAsync</c> on success.</summary>
    IReadOnlyCollection<string> CacheTagsToInvalidate => [];
}

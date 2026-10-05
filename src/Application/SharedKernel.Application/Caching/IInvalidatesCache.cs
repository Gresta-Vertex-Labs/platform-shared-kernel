
namespace SharedKernel.Application.Caching;

/// <summary>
/// Marks a command as declaring the cache entries and tags it renders stale on success.
/// </summary>
/// <remarks>
/// <para>
/// Mirrors <see cref="ICacheableQuery{TValue}"/>'s exact self-supplied pattern — the command
/// instance alone holds the discriminating parameters (for example the entity ID it just mutated), so
/// it computes and supplies its own targets; the invalidation behavior never derives them itself.
/// Never implemented by a query type — this is the write-side half of the read/write caching pair,
/// and <c>SharedKernel.Analyzers</c>' SK0018 reports a query that implements it.
/// </para>
/// <para>
/// Entries are named through <see cref="CacheKeyRef.For{TQuery}(string)"/> rather than as raw key
/// strings, because a query's key is namespaced by its own type. Tag eviction
/// (<see cref="CacheTagsToInvalidate"/>) is the optional second route for a command that invalidates
/// a whole tag rather than enumerating individual entries — both are supported, neither is required
/// if the other is supplied.
/// </para>
/// </remarks>
public interface IInvalidatesCache
{
    /// <summary>Gets the cache entries to remove on success.</summary>
    IReadOnlyCollection<CacheKeyRef> CacheKeysToInvalidate { get; }

    /// <summary>Gets the cache tag(s) to remove on success.</summary>
    IReadOnlyCollection<string> CacheTagsToInvalidate => [];

    /// <summary>
    /// Gets the identity the evicted entries are partitioned by. Defaults to
    /// <see cref="CacheScope.Tenant"/>, and must match the scope declared by the queries being
    /// invalidated — a mismatch evicts a key those queries never wrote.
    /// </summary>
    CacheScope Scope => CacheScope.Tenant;
}

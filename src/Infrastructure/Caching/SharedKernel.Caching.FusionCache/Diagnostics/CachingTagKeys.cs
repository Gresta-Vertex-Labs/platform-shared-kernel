namespace SharedKernel.Caching.FusionCache.Diagnostics;

/// <summary>
/// The tag keys of the cache's metrics and spans. The cross-cutting keys (<c>outcome</c>, <c>error.type</c>,
/// <c>error.code</c>) are in <c>WellKnownTagKeys</c> of <c>SharedKernel.Primitives</c>.
/// </summary>
public static class CachingTagKeys
{
    /// <summary>The low-cardinality part of a cache key, <c>{service}:{entity}</c>; never an id and never a tenant (<c>caching.key_prefix</c>).</summary>
    public const string KeyPrefix = "caching.key_prefix";

    /// <summary>The cache layer that answered, <see cref="CachingTagValues.LevelMemory"/> or <see cref="CachingTagValues.LevelDistributed"/> (<c>caching.level</c>).</summary>
    public const string Level = "caching.level";

    /// <summary>The cache operation, one of the <c>Operation*</c> values of <see cref="CachingTagValues"/> (<c>caching.operation</c>).</summary>
    public const string Operation = "caching.operation";

    /// <summary>Why a memory-cache entry was evicted: the name of the eviction reason (<c>caching.eviction_reason</c>).</summary>
    public const string EvictionReason = "caching.eviction_reason";

    /// <summary>Whether a read was answered, <see cref="CachingTagValues.ResultHit"/> or <see cref="CachingTagValues.ResultMiss"/> (<c>caching.result</c>).</summary>
    public const string Result = "caching.result";
}

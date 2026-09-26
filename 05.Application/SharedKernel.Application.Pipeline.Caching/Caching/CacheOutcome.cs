using SharedKernel.Application.Caching;

namespace SharedKernel.Application.Pipeline.Caching;

/// <summary>What the caching behavior did with one query execution. Internal: a telemetry detail, not a contract.</summary>
/// <remarks>
/// Recorded as the <c>outcome</c> tag on this package's metrics and on the request's
/// <see cref="System.Diagnostics.Activity"/>, so a hit ratio can be read per query type rather than
/// per cache-key prefix.
/// </remarks>
internal enum CacheOutcome
{
    /// <summary>The value was served from the cache; the handler did not run.</summary>
    Hit = 0,

    /// <summary>The value was absent; the handler ran and its value was cached.</summary>
    Miss = 1,

    /// <summary>
    /// The handler ran and its value was written over any existing entry, because the query asked
    /// for it through <see cref="ICacheableQuery.RefreshCache"/>.
    /// </summary>
    Refreshed = 2,

    /// <summary>
    /// The handler ran and nothing was cached, because the result was a failure. A failed result is
    /// never cached, so a transient failure is not pinned for the whole expiry window.
    /// </summary>
    NotCachedFailure = 3,

    /// <summary>
    /// The handler ran and nothing was cached, because the query's
    /// <see cref="ICacheableQuery{TValue}.ShouldCache"/> rejected the value.
    /// </summary>
    NotCachedByPredicate = 4,

    /// <summary>
    /// The cache was not consulted at all, because the declared <see cref="CacheScope"/>'s identity
    /// was not available on this request — a <see cref="CacheScope.Tenant"/> query with no resolved
    /// tenant, or a <see cref="CacheScope.User"/> query with no authenticated user. The handler ran.
    /// </summary>
    BypassedScopeUnavailable = 5,
}

using SharedKernel.Primitives.Logging;

namespace SharedKernel.Application.Caching;

/// <summary>
/// Reserves this package's <c>EventId</c> sub-block within <c>01.Core</c>'s
/// <see cref="LoggingEventIdRanges.Application"/> domain range (5000-5999), per the platform's
/// <c>[LoggerMessage]</c> logging-authoring standard.
/// </summary>
/// <remarks>
/// <c>SharedKernel.Application.Caching</c> is the third package declared in the
/// <c>05.Application</c> domain, so it owns the third 100-wide sub-block, <c>5200</c>-<c>5299</c>,
/// subdivided further by authoring file. Every field is <see langword="const int"/> —
/// <c>[LoggerMessage(EventId = ...)]</c> requires a compile-time constant expression.
/// </remarks>
internal static class CachingBehaviorsLoggingEventIds
{
    // ---- Caching/CachingBehavior.cs (5200-5209) ----

    /// <summary>A query was served from, or written to, the cache (Debug).</summary>
    internal const int LogQueryCacheOutcome = LoggingEventIdRanges.Application + 200;

    /// <summary>
    /// A query declared a scope whose identity was absent on this request, so the cache was not
    /// consulted and the handler ran (Warning). Fail-closed: the entry is never widened to a
    /// scope the query did not declare.
    /// </summary>
    internal const int LogQueryScopeUnavailable = LoggingEventIdRanges.Application + 201;

    // ---- CacheInvalidation/CacheInvalidationBehavior.cs (5210-5219) ----

    /// <summary>Post-commit eviction completed for a command (Debug).</summary>
    internal const int LogInvalidationCompleted = LoggingEventIdRanges.Application + 210;

    /// <summary>
    /// One key or tag could not be evicted after the command had already committed (Error). The
    /// remaining keys and tags are still attempted — the cache now serves a value the committed
    /// write has superseded, until the entry expires on its own.
    /// </summary>
    internal const int LogInvalidationEntryFailed = LoggingEventIdRanges.Application + 211;

    /// <summary>
    /// A command declared a scope whose identity was absent on this request, so nothing was evicted
    /// (Warning). Mirrors <see cref="LogQueryScopeUnavailable"/> on the write side.
    /// </summary>
    internal const int LogInvalidationScopeUnavailable = LoggingEventIdRanges.Application + 212;
}

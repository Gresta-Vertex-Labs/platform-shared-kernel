using SharedKernel.Primitives.Logging;

namespace SharedKernel.Application.Pipeline.Caching.Diagnostics;

/// <summary>
/// The <c>EventId</c>s of <c>SharedKernel.Application.Pipeline.Caching</c>: sub-block 2 (<c>5200</c>-<c>5299</c>) of the
/// <see cref="LoggingEventIdRanges.Application"/> domain range.
/// </summary>
/// <remarks>
/// Every value is a <see langword="const"/> because <c>[LoggerMessage(EventId = ...)]</c> needs a compile-time constant.
/// The numbers are grouped by behavior in tens, and a published number is never reused for another event.
/// </remarks>
internal static class PipelineCachingLoggingEventIds
{
    /// <summary>The first id of the sub-block.</summary>
    internal const int Base = LoggingEventIdRanges.Application + 2 * LoggingEventIdRanges.PackageSubBlockWidth;

    // ---- CachingBehavior (5200-5209) ----

    /// <summary>A query was served from, or written to, the cache (Debug).</summary>
    internal const int QueryCacheResult = Base + 0;

    /// <summary>
    /// A query declared a scope whose identity was absent on this request, so the cache was not consulted and the
    /// handler ran (Warning). Fail-closed: the entry is never widened to a scope the query did not declare.
    /// </summary>
    internal const int QueryScopeUnavailable = Base + 1;

    // ---- CacheInvalidationBehavior (5210-5219) ----

    /// <summary>Post-commit eviction completed for a command (Debug).</summary>
    internal const int InvalidationCompleted = Base + 10;

    /// <summary>
    /// One key or tag could not be evicted after the command had already committed (Error). The remaining keys and tags
    /// are still attempted.
    /// </summary>
    internal const int InvalidationEntryFailed = Base + 11;

    /// <summary>
    /// A command declared a scope whose identity was absent on this request, so nothing was evicted (Warning). Mirrors
    /// <see cref="QueryScopeUnavailable"/> on the write side.
    /// </summary>
    internal const int InvalidationScopeUnavailable = Base + 12;
}

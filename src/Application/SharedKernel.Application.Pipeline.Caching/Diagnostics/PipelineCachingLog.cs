using Microsoft.Extensions.Logging;

namespace SharedKernel.Application.Pipeline.Caching.Diagnostics;

/// <summary>
/// Every log event of <c>SharedKernel.Application.Pipeline.Caching</c>.
/// </summary>
/// <remarks>
/// Plain static partial methods; each caller passes its own <c>ILogger&lt;Behavior&lt;,&gt;&gt;</c>. The cache scope,
/// result and target arrive as the lowercase snake constants of <c>CacheTelemetryNames</c>. No cache key (which carries
/// the tenant id), tenant, user or exception message is a placeholder, and the exception goes last.
/// </remarks>
internal static partial class PipelineCachingLog
{
    [LoggerMessage(
        EventId = PipelineCachingLoggingEventIds.QueryCacheResult,
        Level = LogLevel.Debug,
        Message = "The query '{QueryType}' had the cache result {CacheResult} at the {CacheScope} scope.")]
    internal static partial void QueryCacheResult(ILogger logger, string queryType, string cacheResult, string cacheScope);

    [LoggerMessage(
        EventId = PipelineCachingLoggingEventIds.QueryScopeUnavailable,
        Level = LogLevel.Warning,
        Message = "The query '{QueryType}' declares the {CacheScope} cache scope but that identity is unavailable, so the cache was skipped and the handler ran.")]
    internal static partial void QueryScopeUnavailable(ILogger logger, string queryType, string cacheScope);

    [LoggerMessage(
        EventId = PipelineCachingLoggingEventIds.InvalidationCompleted,
        Level = LogLevel.Debug,
        Message = "The command '{CommandType}' evicted {KeyCount} cache keys and {TagCount} cache tags after the commit.")]
    internal static partial void InvalidationCompleted(ILogger logger, string commandType, int keyCount, int tagCount);

    [LoggerMessage(
        EventId = PipelineCachingLoggingEventIds.InvalidationEntryFailed,
        Level = LogLevel.Error,
        Message = "The command '{CommandType}' could not evict a cache {CacheTarget} after its write had committed, so the cache serves the old value until that entry expires.")]
    internal static partial void InvalidationEntryFailed(ILogger logger, string commandType, string cacheTarget, Exception exception);

    [LoggerMessage(
        EventId = PipelineCachingLoggingEventIds.InvalidationScopeUnavailable,
        Level = LogLevel.Warning,
        Message = "The command '{CommandType}' declares the {CacheScope} cache scope but that identity is unavailable, so the affected entries were not evicted.")]
    internal static partial void InvalidationScopeUnavailable(ILogger logger, string commandType, string cacheScope);
}

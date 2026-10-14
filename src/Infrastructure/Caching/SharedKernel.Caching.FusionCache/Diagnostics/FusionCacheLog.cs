using Microsoft.Extensions.Logging;

namespace SharedKernel.Caching.FusionCache.Diagnostics;

/// <summary>
/// Log statements of the FusionCache package. Keys are logged as <c>{service}:{entity}</c> prefixes and tenant
/// tags as <c>@tenant:{tag}</c>, so an id or a tenant id never reaches a log.
/// </summary>
internal static partial class FusionCacheLog
{
    [LoggerMessage(
        EventId = FusionCacheLoggingEventIds.WaitingForWarmup,
        Level = LogLevel.Information,
        Message = "Host startup waits for cache warmup to complete.")]
    public static partial void WaitingForWarmup(ILogger logger);

    [LoggerMessage(
        EventId = FusionCacheLoggingEventIds.NoWarmupStrategies,
        Level = LogLevel.Information,
        Message = "No cache warmup strategies are registered.")]
    public static partial void NoWarmupStrategies(ILogger logger);

    [LoggerMessage(
        EventId = FusionCacheLoggingEventIds.WarmupStarting,
        Level = LogLevel.Information,
        Message = "Cache warmup is starting with {StrategyCount} strategies.")]
    public static partial void WarmupStarting(ILogger logger, int strategyCount);

    [LoggerMessage(
        EventId = FusionCacheLoggingEventIds.WarmupCancelled,
        Level = LogLevel.Warning,
        Message = "Cache warmup was cancelled before strategy '{StrategyName}'.")]
    public static partial void WarmupCancelled(ILogger logger, string strategyName);

    [LoggerMessage(
        EventId = FusionCacheLoggingEventIds.StrategyExecuting,
        Level = LogLevel.Information,
        Message = "Cache warmup is running strategy '{StrategyName}' with order {Order}.")]
    public static partial void StrategyExecuting(ILogger logger, string strategyName, int order);

    [LoggerMessage(
        EventId = FusionCacheLoggingEventIds.StrategyCompleted,
        Level = LogLevel.Information,
        Message = "Cache warmup strategy '{StrategyName}' completed in {ElapsedMilliseconds} ms.")]
    public static partial void StrategyCompleted(ILogger logger, string strategyName, long elapsedMilliseconds);

    [LoggerMessage(
        EventId = FusionCacheLoggingEventIds.StrategyFailed,
        Level = LogLevel.Error,
        Message = "Cache warmup strategy '{StrategyName}' failed after {ElapsedMilliseconds} ms, and warmup continues with the next strategy.")]
    public static partial void StrategyFailed(ILogger logger, string strategyName, long elapsedMilliseconds, Exception exception);

    [LoggerMessage(
        EventId = FusionCacheLoggingEventIds.WarmupCompleted,
        Level = LogLevel.Information,
        Message = "Cache warmup completed.")]
    public static partial void WarmupCompleted(ILogger logger);

    [LoggerMessage(
        EventId = FusionCacheLoggingEventIds.CacheMiss,
        Level = LogLevel.Debug,
        Message = "Cache miss for '{KeyPrefix}'.")]
    public static partial void CacheMiss(ILogger logger, string keyPrefix);

    [LoggerMessage(
        EventId = FusionCacheLoggingEventIds.CacheSet,
        Level = LogLevel.Debug,
        Message = "Cache set for '{KeyPrefix}'.")]
    public static partial void CacheSet(ILogger logger, string keyPrefix);

    [LoggerMessage(
        EventId = FusionCacheLoggingEventIds.FactoryInvoked,
        Level = LogLevel.Debug,
        Message = "Cache factory invoked for '{KeyPrefix}'.")]
    public static partial void FactoryInvoked(ILogger logger, string keyPrefix);

    [LoggerMessage(
        EventId = FusionCacheLoggingEventIds.CacheRemoved,
        Level = LogLevel.Debug,
        Message = "Cache entry removed for '{KeyPrefix}'.")]
    public static partial void CacheRemoved(ILogger logger, string keyPrefix);

    [LoggerMessage(
        EventId = FusionCacheLoggingEventIds.CacheTagRemoved,
        Level = LogLevel.Debug,
        Message = "Cache entries removed for tag '{Tag}'.")]
    public static partial void CacheTagRemoved(ILogger logger, string tag);

    [LoggerMessage(
        EventId = FusionCacheLoggingEventIds.DecryptFailed,
        Level = LogLevel.Warning,
        Message = "Cache entry for '{KeyPrefix}' failed to decrypt with error code '{ErrorCode}', so it is evicted and treated as a miss.")]
    public static partial void DecryptFailed(ILogger logger, string keyPrefix, string errorCode);

    [LoggerMessage(
        EventId = FusionCacheLoggingEventIds.CacheExpired,
        Level = LogLevel.Debug,
        Message = "Cache entry expired for '{KeyPrefix}'.")]
    public static partial void CacheExpired(ILogger logger, string keyPrefix);

    [LoggerMessage(
        EventId = FusionCacheLoggingEventIds.CacheCleared,
        Level = LogLevel.Warning,
        Message = "Cache cleared, so every entry of this cache was removed.")]
    public static partial void CacheCleared(ILogger logger);

    [LoggerMessage(
        EventId = FusionCacheLoggingEventIds.EvictAfterDecryptFailureFailed,
        Level = LogLevel.Warning,
        Message = "Evicting the undecryptable cache entry for '{KeyPrefix}' failed with {ErrorType}, and a later read retries.")]
    public static partial void EvictAfterDecryptFailureFailed(ILogger logger, string keyPrefix, string errorType, Exception exception);

    [LoggerMessage(
        EventId = FusionCacheLoggingEventIds.ProbeFailed,
        Level = LogLevel.Debug,
        Message = "The cache readiness probe failed with {ErrorType}.")]
    public static partial void ProbeFailed(ILogger logger, string errorType, Exception exception);
}

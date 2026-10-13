using Microsoft.Extensions.Logging;

namespace SharedKernel.Caching.Redis.DistributedLocking.Diagnostics;

/// <summary>
/// Log statements of the Redis lock service. A lock resource is a caller-chosen name that may be logged; callers must
/// never put a tenant id or personal data in it.
/// </summary>
internal static partial class DistributedLockingLog
{
    [LoggerMessage(
        EventId = DistributedLockingLoggingEventIds.LockAcquired,
        Level = LogLevel.Debug,
        Message = "Distributed lock acquired on '{Resource}' with fencing token {FencingToken}.")]
    public static partial void LockAcquired(ILogger logger, string resource, long fencingToken);

    [LoggerMessage(
        EventId = DistributedLockingLoggingEventIds.LockContended,
        Level = LogLevel.Debug,
        Message = "Distributed lock on '{Resource}' is held by another owner.")]
    public static partial void LockContended(ILogger logger, string resource);

    [LoggerMessage(
        EventId = DistributedLockingLoggingEventIds.LeaseAcquired,
        Level = LogLevel.Debug,
        Message = "Lease acquired on '{Resource}' for {Duration} with fencing token {FencingToken}.")]
    public static partial void LeaseAcquired(ILogger logger, string resource, long fencingToken, TimeSpan duration);

    [LoggerMessage(
        EventId = DistributedLockingLoggingEventIds.LeaseContended,
        Level = LogLevel.Debug,
        Message = "Lease on '{Resource}' is held by another owner.")]
    public static partial void LeaseContended(ILogger logger, string resource);

    [LoggerMessage(
        EventId = DistributedLockingLoggingEventIds.LockReleased,
        Level = LogLevel.Debug,
        Message = "Distributed lock on '{Resource}' was released.")]
    public static partial void LockReleased(ILogger logger, string resource);

    [LoggerMessage(
        EventId = DistributedLockingLoggingEventIds.ReleaseFailed,
        Level = LogLevel.Warning,
        Message = "Distributed lock on '{Resource}' could not be released, and it expires on its own.")]
    public static partial void ReleaseFailed(ILogger logger, string resource, Exception exception);

    [LoggerMessage(
        EventId = DistributedLockingLoggingEventIds.ExtendFailed,
        Level = LogLevel.Warning,
        Message = "Distributed lock on '{Resource}' could not be extended, and extension is retried until it would expire.")]
    public static partial void ExtendFailed(ILogger logger, string resource, Exception exception);

    [LoggerMessage(
        EventId = DistributedLockingLoggingEventIds.LockLost,
        Level = LogLevel.Error,
        Message = "Distributed lock on '{Resource}' with fencing token {FencingToken} was lost because of '{LossCause}'.")]
    public static partial void LockLost(ILogger logger, string resource, long fencingToken, LockLossCause lossCause);
}

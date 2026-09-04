using Microsoft.Extensions.Logging;
using SharedKernel.Primitives.Logging;
using SharedKernel.Scheduling.Policies;

namespace SharedKernel.Scheduling.Diagnostics;

/// <summary>
/// Source-generated <c>[LoggerMessage]</c> log statements for this package, in the reserved
/// <c>19000</c>-<c>19999</c> <c>EventId</c> range (<see cref="LoggingEventIdRanges.Scheduling"/>). A
/// single package occupies the whole domain block — no sub-block split is needed (contrast the
/// multi-package <c>02.Caching</c> worked example in <see cref="LoggingEventIdRanges"/>'s own remarks).
/// </summary>
/// <remarks>
/// CorrelationId/TraceId/TenantId are deliberately never explicit message-template placeholders here —
/// they flow ambiently through the OpenTelemetry logging pipeline per the platform-wide logging
/// convention. Callers pass their own <see cref="ILogger"/> instance; this class holds no state.
/// </remarks>
internal static partial class Log
{
    [LoggerMessage(
        EventId = LoggingEventIdRanges.Scheduling + 0,
        Level = LogLevel.Information,
        Message = "Scheduling hosted service started with {RegisteredJobCount} registered job(s)")]
    public static partial void HostedServiceStarted(ILogger logger, int registeredJobCount);

    [LoggerMessage(
        EventId = LoggingEventIdRanges.Scheduling + 1,
        Level = LogLevel.Information,
        Message = "Scheduling hosted service stopping")]
    public static partial void HostedServiceStopping(ILogger logger);

    [LoggerMessage(
        EventId = LoggingEventIdRanges.Scheduling + 2,
        Level = LogLevel.Warning,
        Message = "No IDistributedLockService is registered — SharedKernel.Scheduling is running in "
            + "SINGLE-REPLICA mode. Running more than one replica of this service WILL cause every "
            + "registered job to execute once PER REPLICA, per tick. Register "
            + "SharedKernel.Caching.Redis.DistributedLocking's IDistributedLockService before scaling "
            + "beyond one replica.")]
    public static partial void SingleReplicaWarning(ILogger logger);

    [LoggerMessage(
        EventId = LoggingEventIdRanges.Scheduling + 3,
        Level = LogLevel.Debug,
        Message = "Job '{JobName}' firing (scheduled={ScheduledFireTimeUtc}, actual={ActualFireTimeUtc})")]
    public static partial void JobFireStarted(ILogger logger, string jobName, DateTimeOffset scheduledFireTimeUtc, DateTimeOffset actualFireTimeUtc);

    [LoggerMessage(
        EventId = LoggingEventIdRanges.Scheduling + 4,
        Level = LogLevel.Information,
        Message = "Job '{JobName}' ({CommandType}) succeeded in {Duration}")]
    public static partial void JobFireSucceeded(ILogger logger, string jobName, string commandType, TimeSpan duration);

    [LoggerMessage(
        EventId = LoggingEventIdRanges.Scheduling + 5,
        Level = LogLevel.Warning,
        Message = "Job '{JobName}' ({CommandType}) failed in {Duration}: {ErrorCode} — {ErrorMessage}")]
    public static partial void JobFireFailed(ILogger logger, string jobName, string commandType, TimeSpan duration, string errorCode, string errorMessage);

    [LoggerMessage(
        EventId = LoggingEventIdRanges.Scheduling + 6,
        Level = LogLevel.Error,
        Message = "Job '{JobName}' threw an unhandled exception")]
    public static partial void JobFireThrew(ILogger logger, string jobName, Exception exception);

    [LoggerMessage(
        EventId = LoggingEventIdRanges.Scheduling + 7,
        Level = LogLevel.Information,
        Message = "Job '{JobName}' tick skipped — the previous execution is still running (OverlapPolicy.Skip)")]
    public static partial void JobSkippedOverlap(ILogger logger, string jobName);

    [LoggerMessage(
        EventId = LoggingEventIdRanges.Scheduling + 8,
        Level = LogLevel.Debug,
        Message = "Job '{JobName}' tick queued behind the running execution (OverlapPolicy.Queue, queue depth={QueueDepth})")]
    public static partial void JobQueuedOverlap(ILogger logger, string jobName, int queueDepth);

    [LoggerMessage(
        EventId = LoggingEventIdRanges.Scheduling + 9,
        Level = LogLevel.Warning,
        Message = "Job '{JobName}' misfired — was due at {MissedFireTimeUtc}, first observed at {ObservedAtUtc} (MisfirePolicy={MisfirePolicy})")]
    public static partial void MisfireDetected(ILogger logger, string jobName, DateTimeOffset missedFireTimeUtc, DateTimeOffset observedAtUtc, MisfirePolicy misfirePolicy);

    [LoggerMessage(
        EventId = LoggingEventIdRanges.Scheduling + 10,
        Level = LogLevel.Information,
        Message = "Job '{JobName}' missed occurrence at {MissedFireTimeUtc} discarded (MisfirePolicy.Skip)")]
    public static partial void MisfireSkipped(ILogger logger, string jobName, DateTimeOffset missedFireTimeUtc);

    [LoggerMessage(
        EventId = LoggingEventIdRanges.Scheduling + 11,
        Level = LogLevel.Debug,
        Message = "Job '{JobName}' occurrence claim NOT acquired — another replica already claimed it")]
    public static partial void LockAcquisitionFailed(ILogger logger, string jobName);

    [LoggerMessage(
        EventId = LoggingEventIdRanges.Scheduling + 12,
        Level = LogLevel.Debug,
        Message = "Job '{JobName}' occurrence claim acquired (fencing token={FencingToken})")]
    public static partial void LockAcquired(ILogger logger, string jobName, long? fencingToken);

    [LoggerMessage(
        EventId = LoggingEventIdRanges.Scheduling + 13,
        Level = LogLevel.Critical,
        Message = "Scheduling hosted loop faulted and is stopping")]
    public static partial void HostedServiceFaulted(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = LoggingEventIdRanges.Scheduling + 14,
        Level = LogLevel.Information,
        Message = "Job '{JobName}' execution observed shutdown cancellation")]
    public static partial void JobCancelled(ILogger logger, string jobName);

    [LoggerMessage(
        EventId = LoggingEventIdRanges.Scheduling + 15,
        Level = LogLevel.Warning,
        Message = "Scheduling hosted service shutdown drain timed out waiting for {InFlightCount} in-flight execution(s)")]
    public static partial void ShutdownDrainTimedOut(ILogger logger, int inFlightCount);
}

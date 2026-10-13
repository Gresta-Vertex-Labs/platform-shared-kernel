using Microsoft.Extensions.Logging;
using SharedKernel.Scheduling.Policies;

namespace SharedKernel.Scheduling.Diagnostics;

/// <summary>
/// Source-generated <c>[LoggerMessage]</c> log statements for this package. Every id comes from
/// <see cref="SchedulingLoggingEventIds"/>, the package's sub-block of the reserved <c>19000</c>-<c>19999</c> range.
/// </summary>
/// <remarks>
/// CorrelationId, TraceId and TenantId are deliberately never message-template placeholders: they flow
/// ambiently through the OpenTelemetry logging pipeline. Callers pass their own <see cref="ILogger"/>;
/// this class holds no state.
/// </remarks>
internal static partial class SchedulingLog
{
    [LoggerMessage(
        EventId = SchedulingLoggingEventIds.HostedServiceStarted,
        Level = LogLevel.Information,
        Message = "Scheduling started with {RegisteredJobCount} registered jobs.")]
    public static partial void HostedServiceStarted(ILogger logger, int registeredJobCount);

    [LoggerMessage(
        EventId = SchedulingLoggingEventIds.HostedServiceStopping,
        Level = LogLevel.Information,
        Message = "Scheduling is stopping.")]
    public static partial void HostedServiceStopping(ILogger logger);

    [LoggerMessage(
        EventId = SchedulingLoggingEventIds.SingleReplicaWarning,
        Level = LogLevel.Warning,
        Message = "No IDistributedLockService is registered, so every job runs once per replica; register one before running more than one replica.")]
    public static partial void SingleReplicaWarning(ILogger logger);

    [LoggerMessage(
        EventId = SchedulingLoggingEventIds.JobFireStarted,
        Level = LogLevel.Debug,
        Message = "Job '{JobName}' is firing the occurrence scheduled at {ScheduledFireTimeUtc}, observed at {ActualFireTimeUtc}.")]
    public static partial void JobFireStarted(ILogger logger, string jobName, DateTimeOffset scheduledFireTimeUtc, DateTimeOffset actualFireTimeUtc);

    [LoggerMessage(
        EventId = SchedulingLoggingEventIds.JobFireSucceeded,
        Level = LogLevel.Debug,
        Message = "Job '{JobName}' running '{RequestType}' succeeded in {ElapsedMilliseconds} ms.")]
    public static partial void JobFireSucceeded(ILogger logger, string jobName, string requestType, double elapsedMilliseconds);

    [LoggerMessage(
        EventId = SchedulingLoggingEventIds.JobFireFailed,
        Level = LogLevel.Warning,
        Message = "Job '{JobName}' running '{RequestType}' failed in {ElapsedMilliseconds} ms with {ErrorType} '{ErrorCode}'.")]
    public static partial void JobFireFailed(ILogger logger, string jobName, string requestType, double elapsedMilliseconds, string errorType, string errorCode);

    [LoggerMessage(
        EventId = SchedulingLoggingEventIds.JobFireThrew,
        Level = LogLevel.Error,
        Message = "Job '{JobName}' threw an unhandled exception.")]
    public static partial void JobFireThrew(ILogger logger, string jobName, Exception exception);

    [LoggerMessage(
        EventId = SchedulingLoggingEventIds.JobSkippedOverlap,
        Level = LogLevel.Warning,
        Message = "Job '{JobName}' skipped an occurrence because the previous execution is still running.")]
    public static partial void JobSkippedOverlap(ILogger logger, string jobName);

    [LoggerMessage(
        EventId = SchedulingLoggingEventIds.JobQueuedOverlap,
        Level = LogLevel.Debug,
        Message = "Job '{JobName}' queued an occurrence behind the running execution, with {QueueDepth} queued.")]
    public static partial void JobQueuedOverlap(ILogger logger, string jobName, int queueDepth);

    [LoggerMessage(
        EventId = SchedulingLoggingEventIds.MisfireDetected,
        Level = LogLevel.Warning,
        Message = "Job '{JobName}' missed the occurrence due at {MissedFireTimeUtc}, first observed at {ObservedAtUtc}, under the {MisfirePolicy} policy.")]
    public static partial void MisfireDetected(ILogger logger, string jobName, DateTimeOffset missedFireTimeUtc, DateTimeOffset observedAtUtc, MisfirePolicy misfirePolicy);

    [LoggerMessage(
        EventId = SchedulingLoggingEventIds.MisfireSkipped,
        Level = LogLevel.Warning,
        Message = "Job '{JobName}' discarded the missed occurrence due at {MissedFireTimeUtc}.")]
    public static partial void MisfireSkipped(ILogger logger, string jobName, DateTimeOffset missedFireTimeUtc);

    [LoggerMessage(
        EventId = SchedulingLoggingEventIds.LockAcquisitionFailed,
        Level = LogLevel.Debug,
        Message = "Job '{JobName}' did not claim the occurrence because another replica already did.")]
    public static partial void LockAcquisitionFailed(ILogger logger, string jobName);

    [LoggerMessage(
        EventId = SchedulingLoggingEventIds.LockAcquired,
        Level = LogLevel.Debug,
        Message = "Job '{JobName}' claimed the occurrence with fencing token {FencingToken}.")]
    public static partial void LockAcquired(ILogger logger, string jobName, long? fencingToken);

    [LoggerMessage(
        EventId = SchedulingLoggingEventIds.HostedServiceFaulted,
        Level = LogLevel.Critical,
        Message = "Scheduling loop faulted and is stopping.")]
    public static partial void HostedServiceFaulted(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = SchedulingLoggingEventIds.JobCancelled,
        Level = LogLevel.Information,
        Message = "Job '{JobName}' observed shutdown cancellation.")]
    public static partial void JobCancelled(ILogger logger, string jobName);

    [LoggerMessage(
        EventId = SchedulingLoggingEventIds.ShutdownDrainTimedOut,
        Level = LogLevel.Warning,
        Message = "Shutdown drain timed out while waiting for {InFlightCount} in-flight executions.")]
    public static partial void ShutdownDrainTimedOut(ILogger logger, int inFlightCount);

    [LoggerMessage(
        EventId = SchedulingLoggingEventIds.LockStoreUnavailable,
        Level = LogLevel.Error,
        Message = "Job '{JobName}' skipped the occurrence because the lock store is unavailable, so no replica can claim it.")]
    public static partial void LockStoreUnavailable(ILogger logger, string jobName, Exception exception);

    [LoggerMessage(
        EventId = SchedulingLoggingEventIds.ShutdownDrainFaulted,
        Level = LogLevel.Error,
        Message = "Shutdown drain faulted while waiting for in-flight executions.")]
    public static partial void ShutdownDrainFaulted(ILogger logger, Exception exception);
}

using Microsoft.Extensions.Logging;

namespace SharedKernel.Application.Pipeline.Diagnostics;

/// <summary>
/// Every log event of <c>SharedKernel.Application.Pipeline</c>.
/// </summary>
/// <remarks>
/// Plain static partial methods; each caller passes its own <c>ILogger&lt;Behavior&lt;,&gt;&gt;</c>, so the category is
/// the behavior and never the request type. Request, event, command and handler types are full type names. No cache
/// key, tenant, actor, idempotency key or exception message is a placeholder, and a log written on a caught-exception
/// path passes the exception last.
/// </remarks>
internal static partial class PipelineLog
{
    [LoggerMessage(
        EventId = PipelineLoggingEventIds.Handling,
        Level = LogLevel.Debug,
        Message = "Handling '{RequestType}'.")]
    internal static partial void Handling(ILogger logger, string requestType);

    [LoggerMessage(
        EventId = PipelineLoggingEventIds.HandledSuccess,
        Level = LogLevel.Debug,
        Message = "Handled '{RequestType}' in {ElapsedMilliseconds} milliseconds.")]
    internal static partial void HandledSuccess(ILogger logger, string requestType, double elapsedMilliseconds);

    [LoggerMessage(
        EventId = PipelineLoggingEventIds.HandledSlow,
        Level = LogLevel.Warning,
        Message = "Handled '{RequestType}' in {ElapsedMilliseconds} milliseconds, over the {ThresholdMilliseconds} millisecond threshold.")]
    internal static partial void HandledSlow(ILogger logger, string requestType, double elapsedMilliseconds, double thresholdMilliseconds);

    [LoggerMessage(
        EventId = PipelineLoggingEventIds.HandledFailure,
        Level = LogLevel.Warning,
        Message = "Handled '{RequestType}' with the failure {ErrorType} {ErrorCode} in {ElapsedMilliseconds} milliseconds.")]
    internal static partial void HandledFailure(ILogger logger, string requestType, string errorType, string errorCode, double elapsedMilliseconds);

    [LoggerMessage(
        EventId = PipelineLoggingEventIds.HandlingThrew,
        Level = LogLevel.Error,
        Message = "Handling '{RequestType}' threw after {ElapsedMilliseconds} milliseconds.")]
    internal static partial void HandlingThrew(ILogger logger, string requestType, double elapsedMilliseconds, Exception exception);

    [LoggerMessage(
        EventId = PipelineLoggingEventIds.HandlingCanceled,
        Level = LogLevel.Debug,
        Message = "Handling '{RequestType}' was canceled after {ElapsedMilliseconds} milliseconds.")]
    internal static partial void HandlingCanceled(ILogger logger, string requestType, double elapsedMilliseconds);

    [LoggerMessage(
        EventId = PipelineLoggingEventIds.CommandCallbackFailed,
        Level = LogLevel.Error,
        Message = "A post-commit callback of '{RequestType}' threw.")]
    internal static partial void CommandCallbackFailed(ILogger logger, string requestType, Exception exception);

    [LoggerMessage(
        EventId = PipelineLoggingEventIds.IdempotencyReservationLost,
        Level = LogLevel.Warning,
        Message = "The idempotency reservation for '{RequestType}' was lost before it could be completed, so the response is returned anyway.")]
    internal static partial void IdempotencyReservationLost(ILogger logger, string requestType);

    [LoggerMessage(
        EventId = PipelineLoggingEventIds.IdempotencyStoreUnavailable,
        Message = "Reserving the idempotency key for '{RequestType}' failed in the store, so the request was not handled.")]
    internal static partial void IdempotencyStoreUnavailable(ILogger logger, LogLevel level, string requestType, Exception exception);

    [LoggerMessage(
        EventId = PipelineLoggingEventIds.IdempotencyCompleteFailed,
        Level = LogLevel.Error,
        Message = "Completing the idempotency reservation for '{RequestType}' failed after the handler succeeded, so the response is returned and the reservation expires on its own lease.")]
    internal static partial void IdempotencyCompleteFailed(ILogger logger, string requestType, Exception exception);

    [LoggerMessage(
        EventId = PipelineLoggingEventIds.IdempotencyReleaseFailed,
        Level = LogLevel.Warning,
        Message = "Releasing the idempotency reservation for '{RequestType}' failed, so the original outcome is kept and the reservation expires on its own lease.")]
    internal static partial void IdempotencyReleaseFailed(ILogger logger, string requestType, Exception exception);

    [LoggerMessage(
        EventId = PipelineLoggingEventIds.AuditWriteFailedDuringException,
        Level = LogLevel.Error,
        Message = "Recording the audit entry for the faulted '{RequestType}' failed, so the original exception still propagates.")]
    internal static partial void AuditWriteFailedDuringException(ILogger logger, string requestType, Exception exception);

    [LoggerMessage(
        EventId = PipelineLoggingEventIds.StreamCompleted,
        Level = LogLevel.Debug,
        Message = "Streamed {ItemCount} items of '{RequestType}' in {ElapsedMilliseconds} milliseconds.")]
    internal static partial void StreamCompleted(ILogger logger, string requestType, long itemCount, double elapsedMilliseconds);

    [LoggerMessage(
        EventId = PipelineLoggingEventIds.StreamCanceled,
        Level = LogLevel.Debug,
        Message = "Streaming '{RequestType}' was canceled after {ItemCount} items and {ElapsedMilliseconds} milliseconds.")]
    internal static partial void StreamCanceled(ILogger logger, string requestType, long itemCount, double elapsedMilliseconds);

    [LoggerMessage(
        EventId = PipelineLoggingEventIds.StreamFailedClassified,
        Level = LogLevel.Warning,
        Message = "Streaming '{RequestType}' failed with {ErrorType} {ErrorCode} after {ItemCount} items and {ElapsedMilliseconds} milliseconds.")]
    internal static partial void StreamFailedClassified(
        ILogger logger,
        string requestType,
        string errorType,
        string errorCode,
        long itemCount,
        double elapsedMilliseconds,
        Exception exception);

    [LoggerMessage(
        EventId = PipelineLoggingEventIds.StreamFailed,
        Level = LogLevel.Error,
        Message = "Streaming '{RequestType}' failed after {ItemCount} items and {ElapsedMilliseconds} milliseconds.")]
    internal static partial void StreamFailed(
        ILogger logger,
        string requestType,
        long itemCount,
        double elapsedMilliseconds,
        Exception exception);

    [LoggerMessage(
        EventId = PipelineLoggingEventIds.DomainEventHandlerFailed,
        Message = "The handler '{HandlerType}' failed while handling the domain event '{EventType}'.")]
    internal static partial void DomainEventHandlerFailed(
        ILogger logger,
        LogLevel level,
        string eventType,
        string handlerType,
        Exception exception);

    [LoggerMessage(
        EventId = PipelineLoggingEventIds.TransactionRolledBackByResult,
        Level = LogLevel.Warning,
        Message = "The transaction of '{RequestType}' was rolled back because it returned the failure {ErrorType} {ErrorCode}.")]
    internal static partial void TransactionRolledBackByResult(ILogger logger, string requestType, string errorType, string errorCode);

    [LoggerMessage(
        EventId = PipelineLoggingEventIds.TransactionRolledBackByException,
        Level = LogLevel.Error,
        Message = "The transaction of '{RequestType}' was rolled back because it threw.")]
    internal static partial void TransactionRolledBackByException(ILogger logger, string requestType, Exception exception);

    [LoggerMessage(
        EventId = PipelineLoggingEventIds.TransactionReplayed,
        Level = LogLevel.Warning,
        Message = "The unit of work ran the transaction of '{RequestType}' {AttemptCount} times, so its handler ran again.")]
    internal static partial void TransactionReplayed(ILogger logger, string requestType, int attemptCount);
}

using Microsoft.Extensions.Logging;
using SharedKernel.Application.Pipeline.Shared;

namespace SharedKernel.Application.Pipeline.Diagnostics;

/// <summary>
/// The log events of the stream, domain-event, transaction and idempotency-store-fault paths. The events that predate
/// this class still live in their behaviors.
/// </summary>
/// <remarks>
/// Plain static partial methods; each caller passes its own <c>ILogger&lt;Behavior&gt;</c> so the category is the
/// behavior. Request, event and handler types are full type names; no key, tenant, actor or exception message is a
/// placeholder.
/// </remarks>
internal static partial class PipelineLog
{
    [LoggerMessage(
        EventId = ApplicationBehaviorsLoggingEventIds.LogIdempotencyStoreUnavailable,
        Message = "Reserving the idempotency key for '{RequestType}' failed in the store, so the request was not handled.")]
    internal static partial void IdempotencyStoreUnavailable(ILogger logger, LogLevel level, string requestType, Exception exception);

    [LoggerMessage(
        EventId = ApplicationBehaviorsLoggingEventIds.LogIdempotencyCompleteFailed,
        Level = LogLevel.Error,
        Message = "Completing the idempotency reservation for '{RequestType}' failed after the handler succeeded, so the response is returned and the reservation expires on its own lease.")]
    internal static partial void IdempotencyCompleteFailed(ILogger logger, string requestType, Exception exception);

    [LoggerMessage(
        EventId = ApplicationBehaviorsLoggingEventIds.LogIdempotencyReleaseFailed,
        Level = LogLevel.Warning,
        Message = "Releasing the idempotency reservation for '{RequestType}' failed, so the original outcome is kept and the reservation expires on its own lease.")]
    internal static partial void IdempotencyReleaseFailed(ILogger logger, string requestType, Exception exception);

    [LoggerMessage(
        EventId = ApplicationBehaviorsLoggingEventIds.LogStreamCompleted,
        Level = LogLevel.Debug,
        Message = "Streamed {ItemCount} items of '{RequestType}' in {ElapsedMilliseconds} milliseconds.")]
    internal static partial void StreamCompleted(ILogger logger, string requestType, long itemCount, double elapsedMilliseconds);

    [LoggerMessage(
        EventId = ApplicationBehaviorsLoggingEventIds.LogStreamCanceled,
        Level = LogLevel.Debug,
        Message = "Streaming '{RequestType}' was canceled after {ItemCount} items and {ElapsedMilliseconds} milliseconds.")]
    internal static partial void StreamCanceled(ILogger logger, string requestType, long itemCount, double elapsedMilliseconds);

    [LoggerMessage(
        EventId = ApplicationBehaviorsLoggingEventIds.LogStreamFailedClassified,
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
        EventId = ApplicationBehaviorsLoggingEventIds.LogStreamFailed,
        Level = LogLevel.Error,
        Message = "Streaming '{RequestType}' failed after {ItemCount} items and {ElapsedMilliseconds} milliseconds.")]
    internal static partial void StreamFailed(
        ILogger logger,
        string requestType,
        long itemCount,
        double elapsedMilliseconds,
        Exception exception);

    [LoggerMessage(
        EventId = ApplicationBehaviorsLoggingEventIds.LogDomainEventHandlerFailed,
        Message = "The handler '{HandlerType}' failed while handling the domain event '{EventType}'.")]
    internal static partial void DomainEventHandlerFailed(
        ILogger logger,
        LogLevel level,
        string eventType,
        string handlerType,
        Exception exception);

    [LoggerMessage(
        EventId = ApplicationBehaviorsLoggingEventIds.LogTransactionRolledBackByResult,
        Level = LogLevel.Warning,
        Message = "The transaction of '{RequestType}' was rolled back because it returned the failure {ErrorType} {ErrorCode}.")]
    internal static partial void TransactionRolledBackByResult(ILogger logger, string requestType, string errorType, string errorCode);

    [LoggerMessage(
        EventId = ApplicationBehaviorsLoggingEventIds.LogTransactionRolledBackByException,
        Level = LogLevel.Error,
        Message = "The transaction of '{RequestType}' was rolled back because it threw.")]
    internal static partial void TransactionRolledBackByException(ILogger logger, string requestType, Exception exception);

    [LoggerMessage(
        EventId = ApplicationBehaviorsLoggingEventIds.LogTransactionReplayed,
        Level = LogLevel.Warning,
        Message = "The unit of work ran the transaction of '{RequestType}' {AttemptCount} times, so its handler ran again.")]
    internal static partial void TransactionReplayed(ILogger logger, string requestType, int attemptCount);
}

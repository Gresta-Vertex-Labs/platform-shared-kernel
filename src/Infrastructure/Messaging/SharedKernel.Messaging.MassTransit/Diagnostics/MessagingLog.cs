using Microsoft.Extensions.Logging;

namespace SharedKernel.Messaging.MassTransit.Diagnostics;

/// <summary>
/// Source-generated <c>[LoggerMessage]</c> log statements of the MassTransit core. The ids are
/// <see cref="MessagingLoggingEventIds"/> (7000-7099).
/// </summary>
/// <remarks>
/// The methods take the logger as a parameter because the consumer bases expose it as a property, which the generator
/// cannot use. Correlation id, tenant, actor and exception messages are never log values; the correlation id flows through
/// the log scope.
/// </remarks>
internal static partial class MessagingLog
{
    /// <summary>A <c>ConsumerBase.ConsumeAsync</c> threw; the exception is rethrown.</summary>
    [LoggerMessage(
        EventId = MessagingLoggingEventIds.ConsumeFailed,
        Level = LogLevel.Error,
        Message = "Consuming a message of type '{MessageType}' failed.")]
    internal static partial void ConsumeFailed(ILogger logger, string messageType, Exception exception);

    /// <summary>A batch is about to be processed.</summary>
    [LoggerMessage(
        EventId = MessagingLoggingEventIds.BatchProcessing,
        Level = LogLevel.Debug,
        Message = "Processing a batch of {BatchSize} messages of type '{MessageType}'.")]
    internal static partial void BatchProcessing(ILogger logger, int batchSize, string messageType);

    /// <summary>A <c>BatchConsumerBase.ConsumeAsync</c> threw; the exception is rethrown.</summary>
    [LoggerMessage(
        EventId = MessagingLoggingEventIds.BatchFailed,
        Level = LogLevel.Error,
        Message = "Processing a batch of messages of type '{MessageType}' failed.")]
    internal static partial void BatchFailed(ILogger logger, string messageType, Exception exception);

    /// <summary>A fault message is handed to its fault consumer; the original message is already in the error queue.</summary>
    [LoggerMessage(
        EventId = MessagingLoggingEventIds.FaultHandling,
        Level = LogLevel.Warning,
        Message = "Handling fault '{FaultId}' of a message of type '{MessageType}'.")]
    internal static partial void FaultHandling(ILogger logger, Guid faultId, string messageType);

    /// <summary>A fault consumer threw; the exception is rethrown.</summary>
    [LoggerMessage(
        EventId = MessagingLoggingEventIds.FaultConsumerFailed,
        Level = LogLevel.Error,
        Message = "Fault consumer '{FaultConsumerType}' failed while handling fault '{FaultId}'.")]
    internal static partial void FaultConsumerFailed(ILogger logger, string faultConsumerType, Guid faultId, Exception exception);

    /// <summary>A legacy message is translated to the current schema.</summary>
    [LoggerMessage(
        EventId = MessagingLoggingEventIds.Translating,
        Level = LogLevel.Debug,
        Message = "Translating a message of type '{OldType}' to type '{NewType}'.")]
    internal static partial void Translating(ILogger logger, string oldType, string newType);

    /// <summary>A translator is registered but this service has no consumer for its new schema.</summary>
    [LoggerMessage(
        EventId = MessagingLoggingEventIds.NoConsumerForNewSchema,
        Level = LogLevel.Warning,
        Message = "The translator from '{OldType}' to '{NewType}' has no consumer for the new type in this service, which is fine only when another service consumes it.")]
    internal static partial void NoConsumerForNewSchema(ILogger logger, string oldType, string newType);

    /// <summary>Something registered <c>IRequestContext</c> after the bus was built, so consumers see no caller.</summary>
    [LoggerMessage(
        EventId = MessagingLoggingEventIds.RequestContextOverridden,
        Level = LogLevel.Warning,
        Message = "IRequestContext resolves to '{ResolvedType}' instead of the message-aware context, so call AddSharedKernelRequestContext() before building the messaging bus.")]
    internal static partial void RequestContextOverridden(ILogger logger, string resolvedType);

    /// <summary><c>IRequestContext</c> could not be resolved while checking the registration order.</summary>
    [LoggerMessage(
        EventId = MessagingLoggingEventIds.RequestContextNotResolvable,
        Level = LogLevel.Debug,
        Message = "IRequestContext could not be resolved, so the check of the inbound request context registration was skipped.")]
    internal static partial void RequestContextNotResolvable(ILogger logger, Exception exception);

    /// <summary>A dispatch verb converted a recognised transport fault into a failed result.</summary>
    [LoggerMessage(
        EventId = MessagingLoggingEventIds.DispatchFailed,
        Level = LogLevel.Warning,
        Message = "Dispatching a message of type '{MessageType}' failed during '{Operation}' with error '{ErrorCode}'.")]
    internal static partial void DispatchFailed(ILogger logger, string messageType, string operation, string errorCode, Exception exception);

    /// <summary>The scheduler swallowed a recognised transport fault while cancelling a scheduled message.</summary>
    [LoggerMessage(
        EventId = MessagingLoggingEventIds.SchedulerCancelFailed,
        Level = LogLevel.Warning,
        Message = "Cancelling a scheduled message of type '{MessageType}' failed with a transport fault, so the delivery may still happen.")]
    internal static partial void SchedulerCancelFailed(ILogger logger, string messageType, Exception exception);
}

using Microsoft.Extensions.Logging;

namespace SharedKernel.Caching.Redis.PubSub.Diagnostics;

/// <summary>
/// Log statements of the Redis channel service. A channel name is a caller-chosen name that may be logged; callers
/// must never put a tenant id or personal data in it. Message bodies are never logged.
/// </summary>
internal static partial class PubSubLog
{
    [LoggerMessage(
        EventId = PubSubLoggingEventIds.HandlerFailed,
        Level = LogLevel.Error,
        Message = "Handler for Redis channel '{Channel}' failed, and the subscription continues with the next message.")]
    public static partial void HandlerFailed(ILogger logger, string channel, Exception exception);

    [LoggerMessage(
        EventId = PubSubLoggingEventIds.MessageNotDeserialized,
        Level = LogLevel.Warning,
        Message = "A message on Redis channel '{Channel}' is not a valid '{MessageType}', so it was skipped.")]
    public static partial void MessageNotDeserialized(ILogger logger, string channel, string messageType, Exception exception);

    [LoggerMessage(
        EventId = PubSubLoggingEventIds.UnsubscribeFailed,
        Level = LogLevel.Warning,
        Message = "Unsubscribing from Redis channel '{Channel}' failed.")]
    public static partial void UnsubscribeFailed(ILogger logger, string channel, Exception exception);
}

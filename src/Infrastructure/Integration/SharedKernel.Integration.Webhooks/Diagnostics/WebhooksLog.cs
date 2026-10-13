using Microsoft.Extensions.Logging;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.Integration.Webhooks.Diagnostics;

// Never logs a URL, a host, a header value, a payload or a signing secret. Delivery and subscription ids are
// identifiers of the platform's own records, not personal data.
internal static partial class WebhooksLog
{
    [LoggerMessage(
        EventId = WebhooksLoggingEventIds.ObserverException,
        Level = LogLevel.Error,
        Message = "The webhook delivery observer '{ObserverType}' threw, so the delivery outcome is unaffected.")]
    public static partial void ObserverException(ILogger logger, string observerType, Exception exception);

    [LoggerMessage(
        EventId = WebhooksLoggingEventIds.DeliverySucceeded,
        Level = LogLevel.Debug,
        Message = "Webhook delivery '{DeliveryId}' to subscription '{SubscriptionId}' for event '{EventType}' succeeded after {Attempts} attempts with status code {StatusCode}.")]
    public static partial void DeliverySucceeded(ILogger logger, Guid deliveryId, Guid subscriptionId, string eventType, int attempts, int statusCode);

    [LoggerMessage(
        EventId = WebhooksLoggingEventIds.DeliveryFailed,
        Level = LogLevel.Warning,
        Message = "Webhook delivery '{DeliveryId}' to subscription '{SubscriptionId}' for event '{EventType}' failed after {Attempts} attempts with {ErrorType} error '{ErrorCode}'.")]
    public static partial void DeliveryFailed(ILogger logger, Guid deliveryId, Guid subscriptionId, string eventType, int attempts, ErrorType errorType, string errorCode, Exception? exception);

    [LoggerMessage(
        EventId = WebhooksLoggingEventIds.DeliveryExhausted,
        Level = LogLevel.Warning,
        Message = "Webhook delivery '{DeliveryId}' to subscription '{SubscriptionId}' for event '{EventType}' was exhausted after {Attempts} attempts with {ErrorType} error '{ErrorCode}'.")]
    public static partial void DeliveryExhausted(ILogger logger, Guid deliveryId, Guid subscriptionId, string eventType, int attempts, ErrorType errorType, string errorCode, Exception? exception);

    [LoggerMessage(
        EventId = WebhooksLoggingEventIds.ExhaustionEventNotPublished,
        Level = LogLevel.Warning,
        Message = "The exhaustion event of delivery '{DeliveryId}' for subscription '{SubscriptionId}' was refused with {ErrorType} error '{ErrorCode}', so only the notification is lost.")]
    public static partial void ExhaustionEventNotPublished(ILogger logger, Guid deliveryId, Guid subscriptionId, ErrorType errorType, string errorCode);

    [LoggerMessage(
        EventId = WebhooksLoggingEventIds.DeliveryFaulted,
        Level = LogLevel.Error,
        Message = "Webhook delivery '{DeliveryId}' to subscription '{SubscriptionId}' for event '{EventType}' faulted unexpectedly, so it is reported as failed.")]
    public static partial void DeliveryFaulted(ILogger logger, Guid deliveryId, Guid subscriptionId, string eventType, Exception exception);

    [LoggerMessage(
        EventId = WebhooksLoggingEventIds.ExhaustionPublisherFaulted,
        Level = LogLevel.Error,
        Message = "The event publisher threw while publishing the exhaustion event of delivery '{DeliveryId}' for subscription '{SubscriptionId}', so only the notification is lost.")]
    public static partial void ExhaustionPublisherFaulted(ILogger logger, Guid deliveryId, Guid subscriptionId, Exception exception);
}

using Microsoft.Extensions.Logging;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.Integration.Notifications.Sms.Twilio.Diagnostics;

// Never logs a recipient, a message body, a template value, a URL, the account sid or the auth token. The delivery
// id is the caller's identifier of the send, not personal data.
internal static partial class TwilioLog
{
    [LoggerMessage(
        EventId = TwilioLoggingEventIds.DeliverySucceeded,
        Level = LogLevel.Debug,
        Message = "Twilio SMS delivery '{NotificationDeliveryId}' succeeded.")]
    public static partial void DeliverySucceeded(ILogger logger, Guid notificationDeliveryId);

    [LoggerMessage(
        EventId = TwilioLoggingEventIds.DeliveryFailed,
        Level = LogLevel.Warning,
        Message = "Twilio SMS delivery '{NotificationDeliveryId}' failed with {ErrorType} error '{ErrorCode}' and status code {StatusCode}.")]
    public static partial void DeliveryFailed(ILogger logger, Guid notificationDeliveryId, ErrorType errorType, string errorCode, int? statusCode, Exception? exception);

    [LoggerMessage(
        EventId = TwilioLoggingEventIds.ObserverException,
        Level = LogLevel.Error,
        Message = "The notification delivery observer '{ObserverType}' threw, so the delivery outcome is unaffected.")]
    public static partial void ObserverException(ILogger logger, string observerType, Exception exception);
}

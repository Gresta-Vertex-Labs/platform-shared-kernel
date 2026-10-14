using Microsoft.Extensions.Logging;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.Integration.Notifications.Email.SendGrid.Diagnostics;

// Never logs a recipient, a template value, an attachment name, a URL or the API key. The delivery id is the
// caller's identifier of the send, not personal data.
internal static partial class SendGridLog
{
    [LoggerMessage(
        EventId = SendGridLoggingEventIds.DeliverySucceeded,
        Level = LogLevel.Debug,
        Message = "SendGrid email delivery '{NotificationDeliveryId}' succeeded.")]
    public static partial void DeliverySucceeded(ILogger logger, Guid notificationDeliveryId);

    [LoggerMessage(
        EventId = SendGridLoggingEventIds.DeliveryFailed,
        Level = LogLevel.Warning,
        Message = "SendGrid email delivery '{NotificationDeliveryId}' failed with {ErrorType} error '{ErrorCode}' and status code {StatusCode}.")]
    public static partial void DeliveryFailed(ILogger logger, Guid notificationDeliveryId, ErrorType errorType, string errorCode, int? statusCode, Exception? exception);

    [LoggerMessage(
        EventId = SendGridLoggingEventIds.ObserverException,
        Level = LogLevel.Error,
        Message = "The notification delivery observer '{ObserverType}' threw, so the delivery outcome is unaffected.")]
    public static partial void ObserverException(ILogger logger, string observerType, Exception exception);

    [LoggerMessage(
        EventId = SendGridLoggingEventIds.AttachmentUnresolvable,
        Level = LogLevel.Warning,
        Message = "SendGrid email delivery '{NotificationDeliveryId}' could not read an attachment, with storage error code '{StorageErrorCode}'.")]
    public static partial void AttachmentUnresolvable(ILogger logger, Guid notificationDeliveryId, string storageErrorCode);
}

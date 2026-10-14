using System.Diagnostics;
using SharedKernel.Integration.Notifications.Abstractions.Notifications;
using SharedKernel.Primitives.Telemetry;

namespace SharedKernel.Integration.Notifications.Abstractions.Tracing;

/// <summary>
/// Distributed-tracing source for outbound notification (email/SMS) delivery.
/// </summary>
/// <remarks>
/// <para>
/// A SECOND, independently-instantiated <see cref="ActivitySource"/> object deliberately sharing the identical name
/// string the webhook family uses (<c>WebhookIntegrationActivitySource</c> in <c>SharedKernel.Integration.Webhooks</c>).
/// It lives here because both provider packages already reference this package, and no legal reference path exists
/// between the two independent package families (Notifications must never reference Webhooks, or vice versa) to share
/// one instance. OpenTelemetry subscribes to <see cref="ActivitySource"/> instances purely by name string, so two
/// same-named instances from unrelated packages is correct, not a violation: do not merge them into one static.
/// </para>
/// <para>
/// Each provider sender wraps its <c>SendAsync</c> call in a <c>notification send</c> span (kind Client) via
/// <see cref="StartSend"/>, tagging <see cref="NotificationTagKeys.Channel"/> and
/// <see cref="NotificationTagKeys.DeliveryId"/> up front, then the outcome and failure through the shared Primitives
/// recorder. No span ever carries <c>NotificationMessage.Recipient</c> or a template field as a tag.
/// </para>
/// </remarks>
public static class NotificationIntegrationActivitySource
{
    /// <summary>
    /// The name of the <see cref="ActivitySource"/> used by this package family, identical to the webhook family's
    /// by deliberate design (see remarks).
    /// </summary>
    public const string Name = "SharedKernel.Integration";

    private static readonly ActivitySource Source = new(Name, TelemetryVersion.Of(typeof(NotificationIntegrationActivitySource).Assembly));

    /// <summary>
    /// Starts the span wrapping one provider's <c>INotificationSender.SendAsync</c> call.
    /// </summary>
    /// <param name="channel">The channel the notification is being sent over.</param>
    /// <param name="notificationDeliveryId">The caller-supplied delivery id.</param>
    /// <returns>
    /// The started <see cref="Activity"/>, or <see langword="null"/> when no listener is sampling this source.
    /// </returns>
    public static Activity? StartSend(NotificationChannel channel, Guid notificationDeliveryId)
    {
        var activity = Source.StartActivity("notification send", ActivityKind.Client);
        if (activity is { IsAllDataRequested: true })
        {
            activity.SetTag(NotificationTagKeys.Channel, channel.ToString());
            activity.SetTag(NotificationTagKeys.DeliveryId, notificationDeliveryId);
        }

        return activity;
    }
}

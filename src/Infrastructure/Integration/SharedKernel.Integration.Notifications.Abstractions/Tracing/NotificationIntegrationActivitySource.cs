using System.Diagnostics;
using SharedKernel.Integration.Notifications.Abstractions.Notifications;

namespace SharedKernel.Integration.Notifications.Abstractions.Tracing;

/// <summary>
/// Distributed-tracing source for outbound notification (email/SMS) delivery.
/// </summary>
/// <remarks>
/// <para>
/// A SECOND, independently-instantiated <see cref="ActivitySource"/> object deliberately sharing
/// the identical name string <c>WebhookIntegrationActivitySource</c>
/// (<c>SharedKernel.Integration.Webhooks</c>) already uses. It lives here — in
/// <c>SharedKernel.Integration.Notifications.Abstractions</c> — because both provider packages
/// (<c>.Email.SendGrid</c>, <c>.Sms.Twilio</c>) already reference this package (an ordinary upward
/// reference, not a sibling-to-sibling one), and no legal reference path exists between the two
/// independent package families (Notifications must never reference Webhooks, or vice versa) to
/// share one constant instance. OTel subscribes to <see cref="ActivitySource"/> instances purely by
/// name string at the listener level, so two same-named instances from unrelated packages is
/// correct, not a violation — DO NOT "fix" this into a single shared static across the two families.
/// </para>
/// <para>
/// <c>13.ServiceDefaults</c>'s existing <c>WithIntegrationTelemetry</c> (P-430) already subscribes
/// by this name string — no new <c>13.ServiceDefaults</c> phase is required for notification spans
/// to be collected.
/// </para>
/// <para>
/// Each provider sender wraps its <c>SendAsync</c> call in a <c>"NotificationSender.Send"</c> span
/// via <see cref="StartSend"/>, tagging <see cref="NotificationActivityTags.Channel"/> up front and
/// <see cref="NotificationActivityTags.Outcome"/>/<see cref="NotificationActivityTags.AttemptCount"/>
/// once the terminal outcome is known. No span emitted by either notification provider ever carries
/// <c>NotificationMessage.Recipient</c> or any <c>TemplateModel</c> field as a tag, under any
/// circumstance — the identical no-PII-as-tag discipline the Webhooks family already enforces for
/// its signing secret.
/// </para>
/// </remarks>
public static class NotificationIntegrationActivitySource
{
    /// <summary>
    /// The name of the <see cref="ActivitySource"/> used by this package family — identical to
    /// <c>WebhookIntegrationActivitySource.Name</c> by deliberate design (see remarks).
    /// </summary>
    public const string Name = "SharedKernel.Integration";

    private static readonly ActivitySource Source = new(Name);

    /// <summary>
    /// Starts the span wrapping one provider's <c>INotificationSender.SendAsync</c> call, tagging
    /// <paramref name="channel"/> up front.
    /// </summary>
    /// <param name="channel">The channel the notification is being sent over.</param>
    /// <returns>
    /// The started <see cref="Activity"/>, or <see langword="null"/> when no listener is sampling
    /// this source.
    /// </returns>
    public static Activity? StartSend(NotificationChannel channel)
    {
        var activity = Source.StartActivity("NotificationSender.Send");
        activity?.SetTag(NotificationActivityTags.Channel, channel.ToString());
        return activity;
    }
}

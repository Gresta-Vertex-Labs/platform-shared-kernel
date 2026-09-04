using SharedKernel.Integration.Notifications.Abstractions.Delivery;

namespace SharedKernel.Integration.Notifications.Abstractions.Notifications;

/// <summary>
/// Sends a <see cref="NotificationMessage{TTemplateModel}"/> to its recipient over
/// <see cref="SupportedChannel"/>.
/// </summary>
/// <remarks>
/// Implementations are registered as KEYED services
/// (<c>AddKeyedScoped&lt;INotificationSender, TSender&gt;(NotificationChannel.X)</c>) and resolved by
/// application-layer code via
/// <c>IKeyedServiceProvider.GetRequiredKeyedService&lt;INotificationSender&gt;(message.Channel)</c>.
/// This package defines no router/dispatcher type — unlike <c>IWebhookDispatcher</c>, which owns a
/// genuine fan-out responsibility across N subscriptions, a notification send is always one
/// message to one channel; the channel-selection decision belongs to whatever application-layer
/// code is choosing "send an email" vs. "send an SMS" in the first place.
/// </remarks>
public interface INotificationSender
{
    /// <summary>The single channel this sender implementation serves.</summary>
    NotificationChannel SupportedChannel { get; }

    /// <summary>Sends <paramref name="message"/> to its recipient.</summary>
    /// <typeparam name="TTemplateModel">The strongly-typed model bound into the named template.</typeparam>
    /// <param name="message">The notification to send.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The terminal delivery outcome.</returns>
    /// <remarks>
    /// Never throws for a provider-level send failure (non-2xx, timeout, transport exception, or an
    /// unresolvable attachment <c>FileReference</c>) — those surface as a
    /// <see cref="NotificationDeliveryResult"/> with <c>IsSuccess == false</c>, mirroring
    /// <c>IWebhookDispatcher</c>'s never-throws convention. Only invalid input (null arguments)
    /// throws.
    /// </remarks>
    Task<NotificationDeliveryResult> SendAsync<TTemplateModel>(
        NotificationMessage<TTemplateModel> message,
        CancellationToken ct);
}

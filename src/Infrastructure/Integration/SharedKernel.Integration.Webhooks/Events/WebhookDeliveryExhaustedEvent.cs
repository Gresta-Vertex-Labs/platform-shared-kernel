using SharedKernel.Contracts.Events;

namespace SharedKernel.Integration.Webhooks.Events;

/// <summary>
/// Published exactly once per subscription whose webhook delivery exhausted
/// <c>WebhookDeliveryOptions.MaxAttempts</c> without ever receiving a 2xx response.
/// </summary>
/// <param name="EventId">The unique identifier of this notification event.</param>
/// <param name="OccurredOn">The UTC timestamp at which exhaustion was determined.</param>
/// <param name="SubscriptionId">The subscription that failed to receive the delivery.</param>
/// <param name="EventType">
/// The <see cref="IntegrationEventAttribute"/> name of the original integration event that failed to deliver —
/// the same routing key the subscription matched on.
/// </param>
/// <param name="Attempts">The total number of HTTP attempts made before giving up.</param>
/// <param name="LastError">The error from the final attempt, if any.</param>
/// <remarks>
/// Lets any consumer elsewhere on the platform (an ops/alerting handler, or the owning service
/// itself) react — disable the subscription, page someone, surface it in an admin UI. This type
/// lives here rather than in <c>04.Contracts</c> because it is specific to this capability's own
/// failure mode, not a general cross-service contract. It implements <see cref="IIntegrationEvent"/>
/// and declares an <see cref="IntegrationEventAttribute"/> because <c>IEventPublisher.PublishAsync</c>
/// constrains on <c>where TEvent : class, IIntegrationEvent</c> and refuses an event type with no declared
/// wire name. Its wire name is <see cref="EventName"/>
/// (<c>"sharedkernel.webhooks.delivery-exhausted"</c>), the CloudEvents <c>type</c> of its envelope.
/// </remarks>
[IntegrationEvent(WebhookDeliveryExhaustedEvent.EventName)]
public sealed record WebhookDeliveryExhaustedEvent(
    Guid EventId,
    DateTimeOffset OccurredOn,
    Guid SubscriptionId,
    string EventType,
    int Attempts,
    string? LastError) : IIntegrationEvent
{
    /// <summary>
    /// The event's wire name: <c>sharedkernel.webhooks.delivery-exhausted</c>, identical to the CloudEvents
    /// <c>type</c> of its <see cref="EventEnvelope{TEvent}"/>.
    /// </summary>
    public const string EventName = "sharedkernel.webhooks.delivery-exhausted";
}

using SharedKernel.Contracts.Events;

namespace SharedKernel.Integration.Webhooks.Events;

/// <summary>
/// Published exactly once per subscription whose webhook delivery exhausted
/// <c>WebhookDeliveryOptions.MaxAttempts</c> without ever receiving a 2xx response.
/// </summary>
/// <param name="EventId">The unique identifier of this notification event.</param>
/// <param name="OccurredOn">The UTC timestamp at which exhaustion was determined.</param>
/// <param name="SubscriptionId">The subscription that failed to receive the delivery.</param>
/// <param name="EventType">The original integration event's type name that failed to deliver.</param>
/// <param name="Attempts">The total number of HTTP attempts made before giving up.</param>
/// <param name="LastError">The error from the final attempt, if any.</param>
/// <remarks>
/// Lets any consumer elsewhere on the platform (an ops/alerting handler, or the owning service
/// itself) react — disable the subscription, page someone, surface it in an admin UI. This type
/// lives here rather than in <c>04.Contracts</c> because it is specific to this capability's own
/// failure mode, not a general cross-service contract. It implements <see cref="IIntegrationEvent"/>
/// purely for cross-domain convention consistency (every integration event on the platform exposes
/// the same <c>EventId</c>/<c>OccurredOn</c> shape) — <c>IEventPublisher.PublishAsync</c> constrains
/// only on <c>where TEvent : class</c>, so implementing <see cref="IIntegrationEvent"/> is not
/// required for the publish call to accept this type.
/// </remarks>
public sealed record WebhookDeliveryExhaustedEvent(
    Guid EventId,
    DateTimeOffset OccurredOn,
    Guid SubscriptionId,
    string EventType,
    int Attempts,
    string? LastError) : IIntegrationEvent;

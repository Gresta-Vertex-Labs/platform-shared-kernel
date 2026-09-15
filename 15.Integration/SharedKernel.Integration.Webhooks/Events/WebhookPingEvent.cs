using SharedKernel.Contracts.Events;

namespace SharedKernel.Integration.Webhooks.Events;

/// <summary>
/// A synthetic, no-business-payload integration event used exclusively for subscriber onboarding
/// and connectivity checks.
/// </summary>
/// <param name="EventId">The unique identifier of this ping event.</param>
/// <param name="OccurredOn">The UTC timestamp at which the ping was constructed.</param>
/// <remarks>
/// Constructed and dispatched only by <c>IWebhookDispatcher.SendTestDeliveryAsync</c> — never
/// published onto <c>07.Messaging</c> and never fanned out via <c>DispatchAsync</c>'s normal
/// subscription lookup. Its event type is its <see cref="IntegrationEventAttribute"/> name,
/// <see cref="EventName"/> (<c>"sharedkernel.webhooks.ping"</c>), consistent with <c>IWebhookDispatcher</c>'s
/// routing convention, giving the subscriber an unambiguous, reserved event-type name to distinguish a
/// synthetic onboarding delivery from real business data.
/// </remarks>
[IntegrationEvent(WebhookPingEvent.EventName)]
public sealed record WebhookPingEvent(Guid EventId, DateTimeOffset OccurredOn) : IIntegrationEvent
{
    /// <summary>
    /// The reserved event-type name of a ping delivery: <c>sharedkernel.webhooks.ping</c>, identical to the
    /// CloudEvents <c>type</c> of its <see cref="EventEnvelope{TEvent}"/>.
    /// </summary>
    public const string EventName = "sharedkernel.webhooks.ping";
}

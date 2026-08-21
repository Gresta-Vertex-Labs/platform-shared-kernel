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
/// subscription lookup. Routes as <c>typeof(WebhookPingEvent).Name</c> ("WebhookPingEvent"),
/// consistent with <c>IWebhookDispatcher</c>'s existing <c>typeof(TEvent).Name</c> routing
/// convention, giving the subscriber an unambiguous, reserved event-type name to distinguish a
/// synthetic onboarding delivery from real business data.
/// </remarks>
public sealed record WebhookPingEvent(Guid EventId, DateTimeOffset OccurredOn) : IIntegrationEvent;

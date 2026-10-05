using SharedKernel.Contracts.Events;
using SharedKernel.Integration.Webhooks.Subscriptions;

namespace SharedKernel.Integration.Webhooks.Dispatch;

/// <summary>
/// Delivers integration events to externally-configured webhook subscribers as signed HTTP requests.
/// </summary>
public interface IWebhookDispatcher
{
    /// <summary>
    /// Looks up every active subscription for <typeparamref name="TEvent"/> and delivers
    /// <paramref name="integrationEvent"/> to each of them concurrently, bounded by
    /// <c>WebhookDeliveryOptions.MaxConcurrentDeliveries</c>.
    /// </summary>
    /// <typeparam name="TEvent">The integration event type being dispatched.</typeparam>
    /// <param name="integrationEvent">The event payload to deliver.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>One <see cref="WebhookDeliveryResult"/> per matched subscription.</returns>
    /// <remarks>
    /// The routing key is the <see cref="IntegrationEventAttribute"/> name of the event's runtime type (via
    /// <see cref="IntegrationEventDescriptor"/>) — identical to the CloudEvents <c>type</c> of its
    /// <see cref="EventEnvelope{TEvent}"/> in <c>04.Contracts</c>, so one event routes identically whether it
    /// travels over <c>07.Messaging</c> or as a webhook, and a class rename never silently breaks a
    /// subscription. A single subscription's delivery failure never faults the others — see
    /// <see cref="DispatchToSubscriptionAsync{TEvent}"/>.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="integrationEvent"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// The event's runtime type has no valid <see cref="IntegrationEventAttribute"/>.
    /// </exception>
    Task<IReadOnlyList<WebhookDeliveryResult>> DispatchAsync<TEvent>(TEvent integrationEvent, CancellationToken ct)
        where TEvent : IIntegrationEvent;

    /// <summary>
    /// Delivers <paramref name="integrationEvent"/> to a single, already-resolved subscription.
    /// </summary>
    /// <typeparam name="TEvent">The integration event type being dispatched.</typeparam>
    /// <param name="subscription">The subscription to deliver to.</param>
    /// <param name="integrationEvent">The event payload to deliver.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The terminal delivery outcome for this subscription.</returns>
    /// <remarks>
    /// Exposed publicly for callers that already hold a resolved subscription (e.g. a manual
    /// "redeliver this one" admin action) and don't need the fan-out lookup. Never throws for an
    /// HTTP-level failure (non-2xx, timeout, transport exception) — those surface as a
    /// <see cref="WebhookDeliveryResult"/> with <c>IsSuccess == false</c>. Only invalid input (null
    /// arguments, or an event type with no valid <see cref="IntegrationEventAttribute"/>) throws. The event
    /// type recorded in tracing tags, logs and <c>WebhookDeliveryExhaustedEvent.EventType</c> is the
    /// attribute name of the event's runtime type. On exhausting <c>WebhookDeliveryOptions.MaxAttempts</c>
    /// without a 2xx response, publishes exactly one <c>WebhookDeliveryExhaustedEvent</c> via
    /// <c>IEventPublisher</c> before returning the failed result.
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="subscription"/> or <paramref name="integrationEvent"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The event's runtime type has no valid <see cref="IntegrationEventAttribute"/>.
    /// </exception>
    Task<WebhookDeliveryResult> DispatchToSubscriptionAsync<TEvent>(
        WebhookSubscription subscription,
        TEvent integrationEvent,
        CancellationToken ct)
        where TEvent : IIntegrationEvent;

    /// <summary>
    /// Sends a synthetic onboarding/connectivity-check delivery to <paramref name="subscription"/>.
    /// </summary>
    /// <param name="subscription">The subscription to send the test delivery to.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The terminal delivery outcome, identical in shape to a real event delivery.</returns>
    /// <remarks>
    /// Constructs a <c>WebhookPingEvent</c> and calls
    /// <see cref="DispatchToSubscriptionAsync{TEvent}"/> verbatim — zero parallel signing, retry, or
    /// observer logic. Lets a subscriber verify their endpoint, signature verification, and header
    /// handling before any real business event fires. The delivery's event type is always
    /// <c>"sharedkernel.webhooks.ping"</c> (<c>WebhookPingEvent.EventName</c>), unambiguously distinguishing it
    /// from real business events.
    /// </remarks>
    Task<WebhookDeliveryResult> SendTestDeliveryAsync(WebhookSubscription subscription, CancellationToken ct);
}

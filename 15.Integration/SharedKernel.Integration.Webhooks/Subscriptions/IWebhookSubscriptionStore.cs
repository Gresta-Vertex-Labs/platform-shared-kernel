namespace SharedKernel.Integration.Webhooks.Subscriptions;

/// <summary>
/// Read-only seam through which <c>IWebhookDispatcher</c> looks up the active subscriptions for a
/// given integration event type.
/// </summary>
/// <remarks>
/// Implemented by the consuming microservice — there is no default implementation in this package,
/// and <c>AddSharedKernelWebhooks</c> deliberately does not register one. DI resolution fails at
/// first dispatch if the consuming service omits its own registration, by design.
/// </remarks>
public interface IWebhookSubscriptionStore
{
    /// <summary>
    /// Returns every active subscription that should receive a delivery for
    /// <paramref name="eventType"/>.
    /// </summary>
    /// <param name="eventType">
    /// The routing key: the event's <c>[IntegrationEvent]</c> name (for example
    /// <c>orders.order-placed</c>), identical to the CloudEvents <c>type</c> of its
    /// <c>EventEnvelope&lt;TEvent&gt;</c>. Never the CLR class name.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// The matching subscriptions. Implementations must return only subscriptions where
    /// <c>IsActive == true</c> and whose <c>EventTypes</c> either contains <paramref name="eventType"/>
    /// or is empty (an empty list means "subscribed to everything"). That filtering is the
    /// implementation's responsibility, not the dispatcher's.
    /// </returns>
    Task<IReadOnlyList<WebhookSubscription>> GetActiveSubscriptionsAsync(string eventType, CancellationToken ct);
}

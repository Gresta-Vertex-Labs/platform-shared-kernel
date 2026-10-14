namespace SharedKernel.Integration.Webhooks.Dispatch;

/// <summary>
/// Single source of truth for the span and metric tag keys of the webhook family, domain-local
/// (<c>webhook.{snake_name}</c>). The terminal outcome uses
/// <c>SharedKernel.Primitives.Propagation.WellKnownTagKeys.Outcome</c> with <c>WellKnownOutcomes</c> values, and a
/// cross-domain concept reuses <c>WellKnownTagKeys</c> rather than adding a literal here.
/// </summary>
public static class WebhookTagKeys
{
    /// <summary>The subscription id a span is attributed to. A span tag only, never a metric tag.</summary>
    public const string SubscriptionId = "webhook.subscription_id";

    /// <summary>
    /// The integration event's type: its <c>[IntegrationEvent]</c> name, identical to the CloudEvents <c>type</c> of its
    /// <c>EventEnvelope&lt;TEvent&gt;</c>. Also a metric tag.
    /// </summary>
    public const string EventType = "webhook.event_type";

    /// <summary>The delivery id, stable across retries. A span tag only, never a metric tag.</summary>
    public const string DeliveryId = "webhook.delivery_id";

    /// <summary>The 1-based count of HTTP attempts actually made.</summary>
    public const string AttemptCount = "webhook.attempt_count";

    /// <summary>The number of subscriptions a fan-out dispatch resolved.</summary>
    public const string SubscriptionCount = "webhook.subscription_count";
}

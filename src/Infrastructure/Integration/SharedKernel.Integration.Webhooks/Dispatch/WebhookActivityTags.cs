namespace SharedKernel.Integration.Webhooks.Dispatch;

/// <summary>
/// Single source of truth for the <see cref="System.Diagnostics.Activity.SetTag(string, object?)"/>
/// attribute-key names emitted by <see cref="WebhookIntegrationActivitySource"/>.
/// </summary>
/// <remarks>
/// None of these concepts overlap an existing cross-domain
/// <c>SharedKernel.Primitives.Propagation.WellKnownTagKeys</c> entry (no tenant id, correlation id,
/// or coarse error classification is tagged here today) — every key below is domain-local to this
/// package, following the same single-source-of-truth discipline as
/// <c>WebhookSignatureHeaders</c>. Should a future span here need a cross-domain concept (e.g. a
/// tenant-id tag), reference <c>WellKnownTagKeys</c> instead of adding a duplicate literal here.
/// </remarks>
public static class WebhookActivityTags
{
    /// <summary>The tag carrying the subscription id a span is attributed to.</summary>
    public const string SubscriptionId = "webhook.subscription_id";

    /// <summary>
    /// The tag carrying the integration event's type: its <c>[IntegrationEvent]</c> name, identical to the
    /// CloudEvents <c>type</c> of its <c>EventEnvelope&lt;TEvent&gt;</c>.
    /// </summary>
    public const string EventType = "webhook.event_type";

    /// <summary>The tag carrying the terminal delivery outcome (<c>"success"</c> or <c>"failure"</c>).</summary>
    public const string Outcome = "webhook.outcome";

    /// <summary>The tag carrying the 1-based count of HTTP attempts actually made.</summary>
    public const string AttemptCount = "webhook.attempt_count";

    /// <summary>The tag carrying the number of subscriptions a fan-out dispatch resolved.</summary>
    public const string SubscriptionCount = "webhook.subscription_count";
}

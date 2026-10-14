namespace SharedKernel.Integration.Webhooks.Dispatch;

/// <summary>
/// The error codes a failed <see cref="WebhookDeliveryResult"/> carries, lowercase
/// <c>integration.webhook.{reason}</c>, and the last of which reaches
/// <c>WebhookDeliveryExhaustedEvent.LastErrorCode</c>. The notification family owns the sibling
/// <c>integration.notification.*</c> codes; the two cannot share a type because neither package references the other.
/// </summary>
public static class WebhookErrorCodes
{
    /// <summary>The endpoint answered 5xx, 408 or 429.</summary>
    public const string EndpointUnavailable = "integration.webhook.endpoint_unavailable";

    /// <summary>The endpoint answered any other non-2xx status.</summary>
    public const string EndpointRejected = "integration.webhook.endpoint_rejected";

    /// <summary>The endpoint could not be reached, the circuit is open or the resilience pipeline refused the call.</summary>
    public const string Unavailable = "integration.webhook.unavailable";

    /// <summary>An attempt or the whole delivery timed out.</summary>
    public const string Timeout = "integration.webhook.timeout";

    /// <summary>The configured <c>IWebhookUrlValidator</c> refused the target (the SSRF guard).</summary>
    public const string TargetRejected = "integration.webhook.target_rejected";

    /// <summary>A subscription header collides with a platform signature header.</summary>
    public const string HeaderCollision = "integration.webhook.header_collision";

    /// <summary>The subscription carries no signing secret.</summary>
    public const string SecretsMissing = "integration.webhook.secrets_missing";

    /// <summary>The per-subscription fault boundary caught an unexpected exception.</summary>
    public const string DeliveryFaulted = "integration.webhook.delivery_faulted";
}

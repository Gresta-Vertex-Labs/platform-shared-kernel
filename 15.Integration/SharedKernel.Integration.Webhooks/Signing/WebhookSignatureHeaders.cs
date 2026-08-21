namespace SharedKernel.Integration.Webhooks.Signing;

/// <summary>
/// Single source of truth for the HTTP header names used to carry a webhook's HMAC signature and
/// signing timestamp.
/// </summary>
/// <remarks>
/// Both <see cref="WebhookSignatureProvider"/> (indirectly, via <c>WebhookDispatcher</c> writing
/// outbound headers) and <see cref="WebhookSignatureVerifier"/> (reading inbound headers) must
/// reference these constants — never a literal header-name string — so the dispatch side and the
/// verification side can never silently drift apart.
/// </remarks>
public static class WebhookSignatureHeaders
{
    /// <summary>
    /// The header carrying the lowercase hex HMAC-SHA256 digest produced by
    /// <see cref="WebhookSignatureProvider.Sign"/>.
    /// </summary>
    public const string SignatureHeaderName = "X-Webhook-Signature";

    /// <summary>
    /// The header carrying the Unix-seconds timestamp that was prefixed onto the signing input.
    /// </summary>
    public const string TimestampHeaderName = "X-Webhook-Timestamp";

    /// <summary>
    /// The header carrying the delivery id — a <see cref="Guid"/> generated once per delivery and
    /// held stable across every retry attempt of that delivery, letting the subscriber deduplicate
    /// re-sent requests.
    /// </summary>
    public const string DeliveryIdHeaderName = "X-Webhook-Delivery-Id";
}

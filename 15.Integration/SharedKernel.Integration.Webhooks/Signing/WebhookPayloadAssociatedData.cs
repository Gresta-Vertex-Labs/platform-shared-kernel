using System.Text;

namespace SharedKernel.Integration.Webhooks.Signing;

/// <summary>
/// Single source of truth for the associated-data (AAD) bytes bound into an encrypted webhook
/// payload's AES-GCM authentication tag when <see cref="Options.WebhookDeliveryOptions.EncryptPayload"/>
/// is enabled.
/// </summary>
/// <remarks>
/// <para>
/// Mirrors <see cref="WebhookSignatureProvider"/>'s <c>BuildSigningInput</c> role — one canonical
/// construction shared by both the dispatch side (<c>WebhookDispatcher.SendAsync</c>) and, for a
/// first-party C# subscriber, the decrypt side — so a subscriber never has to hand-roll string
/// concatenation against an undocumented format/separator. Deliberately public, the same reason
/// <see cref="WebhookSignatureVerifier"/> is public.
/// </para>
/// <para>
/// The two components serve different roles and have different reproducibility stories for an
/// external, out-of-platform-control subscriber:
/// </para>
/// <list type="bullet">
/// <item>
/// <description>
/// <c>deliveryId</c>: per-delivery freshness/uniqueness. Reproducible
/// by the subscriber from the already-shipped <see cref="WebhookSignatureHeaders.DeliveryIdHeaderName"/>
/// header, sent on every attempt of a given delivery.
/// </description>
/// </item>
/// <item>
/// <description>
/// <c>subscriptionId</c>: identity binding. Reproducible by the
/// subscriber ONLY out-of-band — through the same pre-established channel that already carries
/// <see cref="Subscriptions.WebhookSubscription.Secrets"/>, never transmitted on the wire. A header
/// carrying the subscription id (e.g. <c>X-Webhook-Subscription-Id</c>) is deliberately never added:
/// AES-GCM's associated-data authentication only defends against a captured-ciphertext replay if the
/// verifying party supplies an AAD value it holds independently of the request being verified —
/// transmitting the identity half alongside the ciphertext would let an attacker simply resupply
/// matching AAD alongside a tampered or cross-subscription-replayed ciphertext, defeating the entire
/// purpose of binding subscription identity into the AAD. This mirrors exactly why
/// <see cref="Subscriptions.WebhookSubscription.Secrets"/> (the HMAC signing key) is never transmitted
/// while the signing timestamp is.
/// </description>
/// </item>
/// </list>
/// </remarks>
public static class WebhookPayloadAssociatedData
{
    /// <summary>
    /// Builds the canonical UTF-8-encoded associated-data bytes for one webhook delivery.
    /// </summary>
    /// <param name="subscriptionId">
    /// The target <see cref="Subscriptions.WebhookSubscription.SubscriptionId"/> — reproducible by
    /// the subscriber only out-of-band, never transmitted on the wire.
    /// </param>
    /// <param name="deliveryId">
    /// The delivery id generated once per delivery by <c>WebhookDispatcher.DispatchToSubscriptionAsync</c>
    /// and held stable across every retry attempt — reproducible by the subscriber from the
    /// <see cref="WebhookSignatureHeaders.DeliveryIdHeaderName"/> header.
    /// </param>
    /// <returns>
    /// The UTF-8 encoding of <c>"{subscriptionId:D}.{deliveryId:D}"</c> — the same canonical format
    /// on both the dispatch side and any first-party C# subscriber's own decrypt path.
    /// </returns>
    public static byte[] Build(Guid subscriptionId, Guid deliveryId) =>
        Encoding.UTF8.GetBytes($"{subscriptionId:D}.{deliveryId:D}");
}

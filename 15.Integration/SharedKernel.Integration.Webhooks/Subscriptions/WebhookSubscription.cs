namespace SharedKernel.Integration.Webhooks.Subscriptions;

/// <summary>
/// A pure data projection of an external party's subscription to one or more integration event
/// types, delivered as signed webhooks.
/// </summary>
/// <param name="SubscriptionId">The unique identifier of the subscription.</param>
/// <param name="Url">The destination URL that signed webhook payloads are POSTed to.</param>
/// <param name="Secrets">
/// Every HMAC-SHA256 signing secret currently considered valid for this subscription, ordered
/// newest-first. A delivery is always signed with <c>Secrets[0]</c> (the newest); verification
/// (<see cref="Signing.WebhookSignatureVerifier"/>) accepts a match against <b>any</b> candidate in
/// the list, supporting a zero-downtime rotation overlap window (issue a new secret → dual-valid
/// window → retire the old secret). Never logged, never sent on the wire, and never surfaced
/// anywhere except as signing/verification input.
/// </param>
/// <param name="EventTypes">
/// The set of event type names this subscription receives — each an event's <c>[IntegrationEvent]</c> name
/// (for example <c>orders.order-placed</c>), identical to the CloudEvents <c>type</c> of its
/// <c>EventEnvelope&lt;TEvent&gt;</c>, never a CLR class name. An empty list means "subscribed to every event type".
/// </param>
/// <param name="IsActive">Whether this subscription currently receives deliveries.</param>
/// <param name="Headers">
/// Optional static headers applied to every outbound delivery for this subscription, in addition to
/// the standard <see cref="Signing.WebhookSignatureHeaders"/>. Defaults to <see langword="null"/>
/// (no additional headers) — existing subscriptions are unaffected. A header name colliding
/// case-insensitively with <see cref="Signing.WebhookSignatureHeaders.SignatureHeaderName"/>,
/// <see cref="Signing.WebhookSignatureHeaders.TimestampHeaderName"/>, or
/// <see cref="Signing.WebhookSignatureHeaders.DeliveryIdHeaderName"/> is rejected at dispatch time —
/// the platform signature headers are never silently overwritten in either direction.
/// </param>
/// <remarks>
/// Pure DTO — no behavior. The consuming service owns persistence of the backing data (typically an
/// EF Core entity via its own <c>06.Persistence</c> stack) and projects it into this record when
/// handing subscriptions to the dispatcher. This package never serializes or stores this type
/// itself.
/// </remarks>
public sealed record WebhookSubscription(
    Guid SubscriptionId,
    Uri Url,
    IReadOnlyList<string> Secrets,
    IReadOnlyList<string> EventTypes,
    bool IsActive,
    IReadOnlyDictionary<string, string>? Headers = null)
{
    /// <summary>
    /// Back-compat single-secret constructor for pre-rotation (v1.0.0) callers. Maps
    /// <paramref name="secret"/> to a one-element <see cref="Secrets"/> list.
    /// </summary>
    /// <param name="subscriptionId">The unique identifier of the subscription.</param>
    /// <param name="url">The destination URL that signed webhook payloads are POSTed to.</param>
    /// <param name="secret">The subscription's single signing secret.</param>
    /// <param name="eventTypes">The set of event type (<c>[IntegrationEvent]</c>) names this subscription receives.</param>
    /// <param name="isActive">Whether this subscription currently receives deliveries.</param>
    [Obsolete(
        "Use the Secrets-list constructor to support zero-downtime signing-secret rotation. This " +
        "overload is retained for back-compat with pre-rotation (v1.0.0) callers and maps to a " +
        "one-element Secrets list.")]
    public WebhookSubscription(Guid subscriptionId, Uri url, string secret, IReadOnlyList<string> eventTypes, bool isActive)
        : this(subscriptionId, url, [secret], eventTypes, isActive)
    {
    }

    /// <summary>
    /// The newest active signing secret — equivalent to <c>Secrets[0]</c>. Retained for back-compat
    /// with pre-rotation (v1.0.0) callers; prefer <see cref="Secrets"/> directly in new code.
    /// </summary>
    [Obsolete("Use Secrets[0] (the newest secret) instead. Retained for back-compat with pre-rotation (v1.0.0) consumers.")]
    public string Secret => Secrets is { Count: > 0 } secrets ? secrets[0] : string.Empty;
}

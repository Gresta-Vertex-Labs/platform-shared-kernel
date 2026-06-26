namespace SharedKernel.Integration.Webhooks.Subscriptions;

/// <summary>
/// A pure data projection of an external party's subscription to one or more integration event
/// types, delivered as signed webhooks.
/// </summary>
/// <param name="SubscriptionId">The unique identifier of the subscription.</param>
/// <param name="Url">The destination URL that signed webhook payloads are POSTed to.</param>
/// <param name="Secret">
/// The shared HMAC-SHA256 key used to sign outbound deliveries. Never logged, never sent on the
/// wire, and never surfaced anywhere except as the input to <c>WebhookSignatureProvider.Sign</c>.
/// </param>
/// <param name="EventTypes">
/// The set of event type names (matching <c>typeof(TEvent).Name</c>) this subscription receives. An
/// empty list means "subscribed to every event type".
/// </param>
/// <param name="IsActive">Whether this subscription currently receives deliveries.</param>
/// <remarks>
/// Pure DTO — no behavior. The consuming service owns persistence of the backing data (typically an
/// EF Core entity via its own <c>06.Persistence</c> stack) and projects it into this record when
/// handing subscriptions to the dispatcher. This package never serializes or stores this type
/// itself.
/// </remarks>
public sealed record WebhookSubscription(
    Guid SubscriptionId,
    Uri Url,
    string Secret,
    IReadOnlyList<string> EventTypes,
    bool IsActive);

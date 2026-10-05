namespace SharedKernel.Integration.Webhooks.Dispatch;

/// <summary>
/// The terminal outcome of attempting to deliver one integration event to one webhook subscription.
/// </summary>
/// <param name="SubscriptionId">The subscription this delivery was attempted against.</param>
/// <param name="DeliveryId">
/// Identifies this delivery — a single <see cref="Guid"/> generated once at the start of the
/// delivery and held stable across every retry attempt of it. Matches the value sent on the wire as
/// <c>WebhookSignatureHeaders.DeliveryIdHeaderName</c>, letting a registered
/// <c>IWebhookDeliveryObserver</c> correlate its own ledger entry to what the subscriber actually
/// received, and letting the subscriber deduplicate re-sent requests.
/// </param>
/// <param name="IsSuccess">
/// <see langword="true"/> only when some attempt within <c>WebhookDeliveryOptions.MaxAttempts</c>
/// received a 2xx response.
/// </param>
/// <param name="StatusCode">
/// The HTTP status code of the final attempt, or <see langword="null"/> if every attempt faulted
/// before a response was received (e.g. timeout, transport exception), or if delivery was rejected
/// before any HTTP attempt was made (e.g. an SSRF-guard rejection or a reserved-header-name
/// collision).
/// </param>
/// <param name="Attempts">
/// The 1-based count of HTTP attempts actually made, or <c>0</c> when delivery was rejected before
/// any HTTP attempt was made.
/// </param>
/// <param name="Error">Populated only when <see cref="IsSuccess"/> is <see langword="false"/>.</param>
/// <remarks>
/// Every terminal outcome other than a successful 2xx — non-2xx exhausted, timeout exhausted,
/// transport exception exhausted, an SSRF-guard rejection, a reserved-header-name collision — is
/// <c>IsSuccess == false</c> with <see cref="Error"/> populated and <see cref="StatusCode"/>
/// reflecting the last attempt if one exists. A delivery failure is always expressed as this
/// record, never as a thrown exception.
/// </remarks>
public sealed record WebhookDeliveryResult(
    Guid SubscriptionId,
    Guid DeliveryId,
    bool IsSuccess,
    int? StatusCode,
    int Attempts,
    string? Error);

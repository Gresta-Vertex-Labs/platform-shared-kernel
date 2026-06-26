namespace SharedKernel.Integration.Webhooks.Dispatch;

/// <summary>
/// The terminal outcome of attempting to deliver one integration event to one webhook subscription.
/// </summary>
/// <param name="SubscriptionId">The subscription this delivery was attempted against.</param>
/// <param name="IsSuccess">
/// <see langword="true"/> only when some attempt within <c>WebhookDeliveryOptions.MaxAttempts</c>
/// received a 2xx response.
/// </param>
/// <param name="StatusCode">
/// The HTTP status code of the final attempt, or <see langword="null"/> if every attempt faulted
/// before a response was received (e.g. timeout, transport exception).
/// </param>
/// <param name="Attempts">The 1-based count of HTTP attempts actually made.</param>
/// <param name="Error">Populated only when <see cref="IsSuccess"/> is <see langword="false"/>.</param>
/// <remarks>
/// Every terminal outcome other than a successful 2xx — non-2xx exhausted, timeout exhausted,
/// transport exception exhausted — is <c>IsSuccess == false</c> with <see cref="Error"/> populated and
/// <see cref="StatusCode"/> reflecting the last attempt if one exists. A delivery failure is always
/// expressed as this record, never as a thrown exception.
/// </remarks>
public sealed record WebhookDeliveryResult(
    Guid SubscriptionId,
    bool IsSuccess,
    int? StatusCode,
    int Attempts,
    string? Error);

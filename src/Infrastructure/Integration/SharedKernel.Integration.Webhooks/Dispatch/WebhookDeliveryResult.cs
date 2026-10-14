using SharedKernel.Primitives.Errors;

namespace SharedKernel.Integration.Webhooks.Dispatch;

/// <summary>
/// The terminal outcome of attempting to deliver one integration event to one webhook subscription.
/// </summary>
/// <remarks>
/// <para>
/// Every terminal outcome other than a successful 2xx (non-2xx exhausted, timeout, transport failure, an SSRF-guard
/// rejection, a reserved-header collision, a missing signing secret, a fault) is <c>IsSuccess == false</c> with
/// <see cref="Error"/> populated, and <see cref="StatusCode"/> reflecting the last attempt if one exists. A delivery
/// failure is always expressed as this record, never as a thrown exception. <see cref="Error"/> is
/// <see langword="null"/> exactly when <see cref="IsSuccess"/> is <see langword="true"/>; its code is one of
/// <see cref="WebhookErrorCodes"/> and its message a fixed sentence.
/// </para>
/// </remarks>
public sealed record WebhookDeliveryResult
{
    /// <summary>Initializes a new instance of <see cref="WebhookDeliveryResult"/>.</summary>
    /// <param name="subscriptionId">The subscription this delivery was attempted against.</param>
    /// <param name="deliveryId">
    /// Identifies this delivery: generated once at its start and stable across every retry. Matches the value sent as
    /// <c>WebhookSignatureHeaders.DeliveryIdHeaderName</c>, so an observer can correlate its ledger entry with what the
    /// subscriber received, and the subscriber can deduplicate re-sent requests.
    /// </param>
    /// <param name="isSuccess">
    /// <see langword="true"/> only when some attempt within <c>WebhookDeliveryOptions.MaxAttempts</c> received a 2xx
    /// response.
    /// </param>
    /// <param name="statusCode">
    /// The HTTP status code of the final attempt, or <see langword="null"/> if no response was received or delivery
    /// was rejected before any HTTP attempt.
    /// </param>
    /// <param name="attempts">
    /// The 1-based count of HTTP attempts actually made, or <c>0</c> when delivery was rejected before any attempt.
    /// </param>
    /// <param name="error">Populated exactly when <paramref name="isSuccess"/> is <see langword="false"/>.</param>
    /// <exception cref="ArgumentException"><paramref name="isSuccess"/> and <paramref name="error"/> disagree.</exception>
    public WebhookDeliveryResult(Guid subscriptionId, Guid deliveryId, bool isSuccess, int? statusCode, int attempts, Error? error)
    {
        if (isSuccess != (error is null))
        {
            throw new ArgumentException("A delivery result carries an error exactly when it failed.", nameof(error));
        }

        SubscriptionId = subscriptionId;
        DeliveryId = deliveryId;
        IsSuccess = isSuccess;
        StatusCode = statusCode;
        Attempts = attempts;
        Error = error;
    }

    /// <summary>The subscription this delivery was attempted against.</summary>
    public Guid SubscriptionId { get; }

    /// <summary>Identifies this delivery; stable across retries and equal to the wire delivery-id header.</summary>
    public Guid DeliveryId { get; }

    /// <summary><see langword="true"/> only when some attempt received a 2xx response.</summary>
    public bool IsSuccess { get; }

    /// <summary>The HTTP status code of the final attempt, when a response was received.</summary>
    public int? StatusCode { get; }

    /// <summary>The 1-based count of HTTP attempts actually made, or <c>0</c> when none was made.</summary>
    public int Attempts { get; }

    /// <summary>The failure, or <see langword="null"/> when <see cref="IsSuccess"/> is <see langword="true"/>.</summary>
    public Error? Error { get; }

    /// <summary>A successful delivery.</summary>
    /// <param name="subscriptionId">The subscription.</param>
    /// <param name="deliveryId">The delivery id.</param>
    /// <param name="statusCode">The 2xx status code.</param>
    /// <param name="attempts">The number of attempts made.</param>
    /// <returns>A result with no <see cref="Error"/>.</returns>
    public static WebhookDeliveryResult Succeeded(Guid subscriptionId, Guid deliveryId, int statusCode, int attempts) =>
        new(subscriptionId, deliveryId, true, statusCode, attempts, null);

    /// <summary>A failed delivery.</summary>
    /// <param name="subscriptionId">The subscription.</param>
    /// <param name="deliveryId">The delivery id.</param>
    /// <param name="error">Why it failed.</param>
    /// <param name="statusCode">The last status code, when a response was received.</param>
    /// <param name="attempts">The number of attempts made.</param>
    /// <returns>A result carrying <paramref name="error"/>.</returns>
    public static WebhookDeliveryResult Failed(Guid subscriptionId, Guid deliveryId, Error error, int? statusCode = null, int attempts = 0)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new WebhookDeliveryResult(subscriptionId, deliveryId, false, statusCode, attempts, error);
    }
}

namespace SharedKernel.Integration.Notifications.Abstractions.Delivery;

/// <summary>
/// The error codes a failed <see cref="NotificationDeliveryResult"/> carries, lowercase
/// <c>integration.notification.{reason}</c>. The webhook family owns the sibling <c>integration.webhook.*</c>
/// codes; the two families cannot share a type because neither may reference the other.
/// </summary>
public static class NotificationErrorCodes
{
    /// <summary>The provider could not be reached, answered 5xx or 429, or the resilience pipeline refused the call.</summary>
    public const string ProviderUnavailable = "integration.notification.provider_unavailable";

    /// <summary>The provider did not answer in time.</summary>
    public const string ProviderTimeout = "integration.notification.provider_timeout";

    /// <summary>The provider refused the message (any other non-2xx answer).</summary>
    public const string ProviderRejected = "integration.notification.provider_rejected";

    /// <summary>An attachment could not be opened or downloaded from storage.</summary>
    public const string AttachmentUnresolvable = "integration.notification.attachment_unresolvable";
}

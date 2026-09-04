namespace SharedKernel.Integration.Notifications.Abstractions.Delivery;

/// <summary>
/// The terminal outcome of attempting to send one
/// <see cref="Notifications.NotificationMessage{TTemplateModel}"/>.
/// </summary>
/// <param name="NotificationDeliveryId">Echoes the caller-supplied id from the originating message.</param>
/// <param name="IsSuccess"><see langword="true"/> only when the provider accepted the send.</param>
/// <param name="ProviderMessageId">
/// The vendor's own message/SID id, for correlating with a future provider delivery-status
/// callback — out of scope today.
/// </param>
/// <param name="Error">Populated only when <paramref name="IsSuccess"/> is <see langword="false"/>.</param>
/// <remarks>
/// Deliberately NOT <c>Result&lt;T&gt;</c>-wrapped — a domain-internal parallel to
/// <c>WebhookDeliveryResult</c>'s established shape, for consistency within <c>15.Integration</c>,
/// even though this package's own <c>SharedKernel.Storage.Abstractions</c> dependency uses
/// <c>Result&lt;T&gt;</c> internally.
/// </remarks>
public sealed record NotificationDeliveryResult(
    Guid NotificationDeliveryId,
    bool IsSuccess,
    string? ProviderMessageId,
    string? Error);

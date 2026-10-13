using SharedKernel.Primitives.Errors;

namespace SharedKernel.Integration.Notifications.Abstractions.Delivery;

/// <summary>
/// The terminal outcome of attempting to send one
/// <see cref="Notifications.NotificationMessage{TTemplateModel}"/>.
/// </summary>
/// <remarks>
/// Deliberately NOT <c>Result&lt;T&gt;</c>-wrapped: the record also carries the delivery id and the provider
/// message id, mirroring <c>WebhookDeliveryResult</c>. <see cref="Error"/> is <see langword="null"/> exactly when
/// <see cref="IsSuccess"/> is <see langword="true"/>; a failure is always an <see cref="Primitives.Errors.Error"/>
/// with an <c>integration.notification.*</c> code (<see cref="NotificationErrorCodes"/>), never free text.
/// </remarks>
public sealed record NotificationDeliveryResult
{
    /// <summary>Initializes a new instance of <see cref="NotificationDeliveryResult"/>.</summary>
    /// <param name="notificationDeliveryId">Echoes the caller-supplied id from the originating message.</param>
    /// <param name="isSuccess"><see langword="true"/> only when the provider accepted the send.</param>
    /// <param name="providerMessageId">The vendor's own message id, for correlating with a provider status callback.</param>
    /// <param name="error">Populated exactly when <paramref name="isSuccess"/> is <see langword="false"/>.</param>
    /// <exception cref="ArgumentException"><paramref name="isSuccess"/> and <paramref name="error"/> disagree.</exception>
    public NotificationDeliveryResult(Guid notificationDeliveryId, bool isSuccess, string? providerMessageId, Error? error)
    {
        if (isSuccess != (error is null))
        {
            throw new ArgumentException("A delivery result carries an error exactly when it failed.", nameof(error));
        }

        NotificationDeliveryId = notificationDeliveryId;
        IsSuccess = isSuccess;
        ProviderMessageId = providerMessageId;
        Error = error;
    }

    /// <summary>Echoes the caller-supplied id from the originating message.</summary>
    public Guid NotificationDeliveryId { get; }

    /// <summary><see langword="true"/> only when the provider accepted the send.</summary>
    public bool IsSuccess { get; }

    /// <summary>
    /// The vendor's own message/SID id, for correlating with a future provider delivery-status callback.
    /// </summary>
    public string? ProviderMessageId { get; }

    /// <summary>The failure, or <see langword="null"/> when <see cref="IsSuccess"/> is <see langword="true"/>.</summary>
    public Error? Error { get; }

    /// <summary>A successful delivery.</summary>
    /// <param name="notificationDeliveryId">The caller-supplied delivery id.</param>
    /// <param name="providerMessageId">The vendor's message id, when it returned one.</param>
    /// <returns>A result with no <see cref="Error"/>.</returns>
    public static NotificationDeliveryResult Succeeded(Guid notificationDeliveryId, string? providerMessageId = null) =>
        new(notificationDeliveryId, true, providerMessageId, null);

    /// <summary>A failed delivery.</summary>
    /// <param name="notificationDeliveryId">The caller-supplied delivery id.</param>
    /// <param name="error">Why it failed.</param>
    /// <returns>A result carrying <paramref name="error"/>.</returns>
    public static NotificationDeliveryResult Failed(Guid notificationDeliveryId, Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new NotificationDeliveryResult(notificationDeliveryId, false, null, error);
    }
}

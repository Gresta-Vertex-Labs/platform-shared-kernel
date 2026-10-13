using System.Globalization;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.Integration.Notifications.Abstractions.Delivery;

/// <summary>
/// The <see cref="Error"/> factory of the notification family, shared by both provider packages. Every message is
/// a fixed sentence: no provider message, host, recipient or exception text ever reaches an <see cref="Error"/>.
/// </summary>
public static class NotificationErrors
{
    /// <summary>The provider could not be reached, or the resilience pipeline refused the call.</summary>
    /// <returns>An <see cref="ErrorType.Unavailable"/> error.</returns>
    public static Error ProviderUnavailable() =>
        Error.Unavailable(NotificationErrorCodes.ProviderUnavailable, "The notification provider is unavailable.");

    /// <summary>The provider answered with an overload or server error status.</summary>
    /// <param name="statusCode">The numeric HTTP status code.</param>
    /// <returns>An <see cref="ErrorType.Unavailable"/> error.</returns>
    public static Error ProviderUnavailable(int statusCode) =>
        Error.Unavailable(
            NotificationErrorCodes.ProviderUnavailable,
            string.Create(CultureInfo.InvariantCulture, $"The notification provider is unavailable and answered with status code {statusCode}."));

    /// <summary>The provider did not answer in time.</summary>
    /// <returns>An <see cref="ErrorType.Timeout"/> error.</returns>
    public static Error ProviderTimeout() =>
        Error.Timeout(NotificationErrorCodes.ProviderTimeout, "The notification provider did not answer in time.");

    /// <summary>The provider refused the message.</summary>
    /// <param name="statusCode">The numeric HTTP status code.</param>
    /// <returns>An <see cref="ErrorType.Unexpected"/> error.</returns>
    public static Error ProviderRejected(int statusCode) =>
        Error.Unexpected(
            NotificationErrorCodes.ProviderRejected,
            string.Create(CultureInfo.InvariantCulture, $"The notification provider rejected the message with status code {statusCode}."));

    /// <summary>Maps a non-success HTTP status code onto the matching provider error.</summary>
    /// <param name="statusCode">The numeric HTTP status code.</param>
    /// <returns>An unavailable error for 5xx and 429, otherwise a rejected error.</returns>
    public static Error ForStatusCode(int statusCode) =>
        statusCode >= 500 || statusCode == 429 ? ProviderUnavailable(statusCode) : ProviderRejected(statusCode);

    /// <summary>An attachment could not be opened or downloaded from storage.</summary>
    /// <returns>An <see cref="ErrorType.Validation"/> error.</returns>
    public static Error AttachmentUnresolvable() =>
        Error.Validation(NotificationErrorCodes.AttachmentUnresolvable, "One or more attachments could not be resolved from storage.");
}

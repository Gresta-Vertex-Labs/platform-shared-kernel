using System.Globalization;
using Polly;
using Polly.Timeout;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.Integration.Webhooks.Dispatch;

/// <summary>
/// The <see cref="Error"/> factory of a webhook delivery. Every message is a fixed sentence: a transport exception
/// names the subscriber host, so only its <em>kind</em> reaches an error, and the exception object goes to the log
/// call and the span instead.
/// </summary>
internal static class WebhookErrors
{
    /// <summary>The endpoint answered with a non-2xx status code.</summary>
    /// <param name="statusCode">The numeric HTTP status code.</param>
    public static Error EndpointResponded(int statusCode)
    {
        var message = string.Create(CultureInfo.InvariantCulture, $"The webhook endpoint answered with status code {statusCode}.");
        return statusCode >= 500 || statusCode is 408 or 429
            ? Error.Unavailable(WebhookErrorCodes.EndpointUnavailable, message)
            : Error.Unexpected(WebhookErrorCodes.EndpointRejected, message);
    }

    /// <summary>The endpoint could not be reached, or the resilience pipeline refused the call.</summary>
    public static Error Unavailable() =>
        Error.Unavailable(WebhookErrorCodes.Unavailable, "The webhook endpoint could not be reached.");

    /// <summary>The delivery timed out.</summary>
    public static Error Timeout() =>
        Error.Timeout(WebhookErrorCodes.Timeout, "The webhook delivery timed out.");

    /// <summary>The URL validator refused the target.</summary>
    public static Error TargetRejected() =>
        Error.Validation(WebhookErrorCodes.TargetRejected, "The webhook target was rejected by the configured URL validator.");

    /// <summary>A subscription header collides with a platform signature header.</summary>
    public static Error HeaderCollision() =>
        Error.Validation(WebhookErrorCodes.HeaderCollision, "A subscription header collides with a reserved webhook signature header.");

    /// <summary>The subscription carries no signing secret.</summary>
    public static Error SecretsMissing() =>
        Error.Validation(WebhookErrorCodes.SecretsMissing, "The webhook subscription has no signing secrets configured.");

    /// <summary>The fault boundary caught an unexpected exception.</summary>
    public static Error DeliveryFaulted() =>
        Error.Unexpected(WebhookErrorCodes.DeliveryFaulted, "The webhook delivery failed unexpectedly.");

    /// <summary>Maps a caught transport exception onto its error.</summary>
    /// <param name="exception">The transport-level exception.</param>
    public static Error ForTransportFault(Exception exception) =>
        exception switch
        {
            TimeoutRejectedException => Timeout(),
            ExecutionRejectedException => Unavailable(),
            TimeoutException or TaskCanceledException => Timeout(),
            _ => Unavailable(),
        };
}

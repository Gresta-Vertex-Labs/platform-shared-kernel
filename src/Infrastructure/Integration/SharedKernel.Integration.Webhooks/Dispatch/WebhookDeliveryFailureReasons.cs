using System.Globalization;
using Polly;
using Polly.Timeout;

namespace SharedKernel.Integration.Webhooks.Dispatch;

/// <summary>
/// The closed set of failure sentences a <see cref="WebhookDeliveryResult"/> (and the exhaustion event's
/// <c>LastError</c>) can carry. No sentence is built from an exception message, a host, a URL or a header value:
/// a transport exception names the subscriber host, so only its <em>kind</em> reaches the result.
/// </summary>
internal static class WebhookDeliveryFailureReasons
{
    /// <summary>The endpoint did not answer in time.</summary>
    public const string TimedOut = "Webhook delivery timed out.";

    /// <summary>The endpoint could not be reached.</summary>
    public const string EndpointUnreachable = "Webhook endpoint could not be reached.";

    /// <summary>The resilience pipeline refused the call (open circuit, rate limiter).</summary>
    public const string RejectedByPipeline = "Webhook delivery was rejected by the resilience pipeline.";

    /// <summary>The URL validator refused the target.</summary>
    public const string TargetRejected = "Webhook delivery target was rejected by the configured URL validator.";

    /// <summary>A subscription header collides with a platform signature header.</summary>
    public const string HeaderCollision = "A subscription header collides with a reserved webhook signature header and was rejected.";

    /// <summary>The subscription carries no signing secret.</summary>
    public const string SigningSecretsMissing = "Webhook subscription has no signing secrets configured.";

    /// <summary>An unexpected exception was caught by the per-subscription fault boundary.</summary>
    public const string DeliveryFaulted = "Webhook delivery failed unexpectedly.";

    /// <summary>The endpoint answered with a non-2xx status code.</summary>
    /// <param name="statusCode">The numeric HTTP status code.</param>
    public static string EndpointResponded(int statusCode) =>
        string.Create(CultureInfo.InvariantCulture, $"Webhook endpoint responded with status code {statusCode}.");

    /// <summary>Maps a caught transport exception onto its fixed reason sentence.</summary>
    /// <param name="exception">The transport-level exception.</param>
    public static string ForTransportFault(Exception exception) =>
        exception switch
        {
            TimeoutRejectedException => TimedOut,
            ExecutionRejectedException => RejectedByPipeline,
            TimeoutException or TaskCanceledException => TimedOut,
            _ => EndpointUnreachable,
        };
}

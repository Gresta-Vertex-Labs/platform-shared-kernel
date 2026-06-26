using System.Net.Http;

namespace SharedKernel.Integration.Webhooks.Dispatch;

/// <summary>
/// Per-request mutable counter that records how many HTTP attempts the resilience pipeline made,
/// surfaced back to <see cref="WebhookDispatcher"/> after <c>HttpClient.SendAsync</c> returns.
/// </summary>
/// <remarks>
/// Attached to each outgoing <see cref="HttpRequestMessage"/> via <see cref="Key"/> and incremented
/// from <c>HttpStandardResilienceOptions.Retry.OnRetry</c>, which is configured once at registration
/// time in <c>AddSharedKernelWebhooks</c> — this is bookkeeping for the platform-managed retry
/// pipeline, not a hand-rolled retry loop.
/// </remarks>
internal sealed class WebhookAttemptTracker
{
    /// <summary>The <see cref="HttpRequestOptionsKey{TValue}"/> used to attach a tracker to a request.</summary>
    public static readonly HttpRequestOptionsKey<WebhookAttemptTracker> Key = new("SharedKernel.Integration.Webhooks.AttemptTracker");

    private int _attempts = 1;

    /// <summary>The total number of HTTP attempts made so far for this request, 1-based.</summary>
    public int Attempts => _attempts;

    /// <summary>Records that another attempt was made.</summary>
    public void RecordRetry() => Interlocked.Increment(ref _attempts);
}

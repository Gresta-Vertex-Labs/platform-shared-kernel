namespace SharedKernel.Integration.Webhooks.Dispatch;

/// <summary>
/// Decides whether a webhook subscription's target <see cref="Uri"/> is an acceptable outbound
/// delivery destination — the SSRF (server-side request forgery) guard.
/// </summary>
/// <remarks>
/// Invoked by <see cref="WebhookDispatcher"/> immediately before every outbound send — re-checked
/// per delivery, never cached from subscription-registration time, to close the DNS-rebinding
/// bypass where a hostname resolves to a public IP at validation time and a private one at
/// connection time. A <see langword="false"/> result surfaces as a failed, non-throwing
/// <see cref="WebhookDeliveryResult"/> — never a thrown exception. Overridable via
/// <c>WithUrlValidator&lt;T&gt;()</c>.
/// </remarks>
public interface IWebhookUrlValidator
{
    /// <summary>
    /// Determines whether <paramref name="url"/> is an acceptable outbound delivery destination.
    /// </summary>
    /// <param name="url">The subscription's target URL.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// <see langword="true"/> when delivery to <paramref name="url"/> is permitted; otherwise
    /// <see langword="false"/>.
    /// </returns>
    Task<bool> ValidateAsync(Uri url, CancellationToken ct);
}

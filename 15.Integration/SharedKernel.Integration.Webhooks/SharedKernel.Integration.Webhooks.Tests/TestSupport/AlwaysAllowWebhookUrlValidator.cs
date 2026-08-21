using SharedKernel.Integration.Webhooks.Dispatch;

namespace SharedKernel.Integration.Webhooks.Tests.TestSupport;

/// <summary>
/// Permissive <see cref="IWebhookUrlValidator"/> test double that always allows the target through
/// — no real DNS lookup, no real network call. Installed as <see cref="WebhookTestHarness"/>'s
/// default so every test not specifically exercising the SSRF guard is unaffected by it; the tests
/// that DO exercise the guard override this registration via <c>configureServices</c>.
/// </summary>
internal sealed class AlwaysAllowWebhookUrlValidator : IWebhookUrlValidator
{
    public Task<bool> ValidateAsync(Uri url, CancellationToken ct) => Task.FromResult(true);
}

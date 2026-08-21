using SharedKernel.Integration.Webhooks.Dispatch;

namespace SharedKernel.Integration.Webhooks.Tests.TestSupport;

/// <summary>
/// <see cref="IWebhookUrlValidator"/> test double that always rejects the target — no real DNS
/// lookup, no real network call. Used to prove <see cref="WebhookDispatcher"/>'s rejection path
/// (non-throwing failed result, observer notification, no HTTP attempt).
/// </summary>
internal sealed class RejectingWebhookUrlValidator : IWebhookUrlValidator
{
    public int CallCount { get; private set; }

    public Task<bool> ValidateAsync(Uri url, CancellationToken ct)
    {
        CallCount++;
        return Task.FromResult(false);
    }
}

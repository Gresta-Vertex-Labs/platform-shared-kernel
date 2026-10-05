using SharedKernel.Integration.Webhooks.Subscriptions;

namespace SharedKernel.Integration.Webhooks.Tests.TestSupport;

/// <summary>
/// In-memory <see cref="IWebhookSubscriptionStore"/> implementing the documented filtering
/// contract (active only; matches event type or has an empty <c>EventTypes</c> list).
/// </summary>
internal sealed class FakeWebhookSubscriptionStore : IWebhookSubscriptionStore
{
    private readonly List<WebhookSubscription> _subscriptions;

    public FakeWebhookSubscriptionStore(IEnumerable<WebhookSubscription> subscriptions)
    {
        _subscriptions = subscriptions.ToList();
    }

    public Task<IReadOnlyList<WebhookSubscription>> GetActiveSubscriptionsAsync(string eventType, CancellationToken ct)
    {
        IReadOnlyList<WebhookSubscription> matches = _subscriptions
            .Where(s => s.IsActive && (s.EventTypes.Count == 0 || s.EventTypes.Contains(eventType)))
            .ToList();

        return Task.FromResult(matches);
    }
}

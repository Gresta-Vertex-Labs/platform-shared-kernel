using System.Collections.Concurrent;
using SharedKernel.Integration.Webhooks.Dispatch;
using SharedKernel.Integration.Webhooks.Observability;
using SharedKernel.Integration.Webhooks.Subscriptions;

namespace SharedKernel.Testing.Integration;

/// <summary>
/// In-memory test double for <see cref="IWebhookDeliveryObserver"/>. Records every
/// <see cref="OnAttemptAsync"/>/<see cref="OnCompletedAsync"/> call for later assertion.
/// </summary>
/// <remarks>
/// Never throws — this honestly models production's own documented contract ("an observer's
/// exception is caught and logged... never allowed to fault the delivery pipeline") by construction,
/// rather than requiring a try/catch this fake would otherwise need to separately prove. Records
/// every call — including when no assertion is ever made — into thread-safe collections; the
/// <c>Should*</c> assertion helpers are read-only queries over those collections and never mutate
/// them.
/// </remarks>
public sealed class InMemoryWebhookDeliveryObserver : IWebhookDeliveryObserver
{
    private readonly ConcurrentQueue<(WebhookSubscription Subscription, int AttemptNumber)> _attempts = new();
    private readonly ConcurrentQueue<(WebhookSubscription Subscription, WebhookDeliveryResult Result)> _completions = new();

    /// <inheritdoc />
    public Task OnAttemptAsync(WebhookSubscription subscription, int attemptNumber, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(subscription);
        _attempts.Enqueue((subscription, attemptNumber));
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task OnCompletedAsync(WebhookSubscription subscription, WebhookDeliveryResult result, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(subscription);
        ArgumentNullException.ThrowIfNull(result);
        _completions.Enqueue((subscription, result));
        return Task.CompletedTask;
    }

    /// <summary>Every attempt recorded via <see cref="OnAttemptAsync"/>, in call order.</summary>
    public IReadOnlyList<(WebhookSubscription Subscription, int AttemptNumber)> Attempts => [.. _attempts];

    /// <summary>Every completion recorded via <see cref="OnCompletedAsync"/>, in call order.</summary>
    public IReadOnlyList<(WebhookSubscription Subscription, WebhookDeliveryResult Result)> Completions => [.. _completions];

    /// <summary>Asserts that at least one delivery attempt was observed for the given subscription id.</summary>
    /// <param name="subscriptionId">The subscription id expected to have an observed attempt.</param>
    /// <exception cref="InvalidOperationException">No matching attempt was recorded.</exception>
    public void ShouldHaveObservedAttempt(Guid subscriptionId)
    {
        var found = _attempts.Any(entry => entry.Subscription.SubscriptionId == subscriptionId);
        if (!found)
        {
            throw new InvalidOperationException(
                $"Expected an observed delivery attempt for subscription '{subscriptionId}' but none was found.");
        }
    }

    /// <summary>Returns the first recorded completion for the given subscription id.</summary>
    /// <param name="subscriptionId">The subscription id expected to have an observed completion.</param>
    /// <returns>The recorded delivery result.</returns>
    /// <exception cref="InvalidOperationException">No matching completion was recorded.</exception>
    public WebhookDeliveryResult ShouldHaveObservedCompletion(Guid subscriptionId)
    {
        foreach (var (subscription, result) in _completions)
        {
            if (subscription.SubscriptionId == subscriptionId)
            {
                return result;
            }
        }

        throw new InvalidOperationException(
            $"Expected an observed delivery completion for subscription '{subscriptionId}' but none was found.");
    }
}

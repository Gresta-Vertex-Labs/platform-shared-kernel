using SharedKernel.Integration.Webhooks.Dispatch;
using SharedKernel.Integration.Webhooks.Subscriptions;
using SharedKernel.Testing.Integration;
using Xunit;

namespace SharedKernel.Testing.SelfTests.Integration;

/// <summary>
/// Proves <see cref="InMemoryWebhookDeliveryObserver"/> against <c>IWebhookDeliveryObserver</c>'s
/// documented contract — no consuming domain has adopted this fake yet (see
/// <c>src/Testing/state-map.md</c> T-86), so this self-test is the only behavioral proof today, per
/// the SelfTests routing rule.
/// </summary>
public sealed class InMemoryWebhookDeliveryObserverTests
{
    private static WebhookSubscription CreateSubscription(Guid? subscriptionId = null) =>
        new(
            subscriptionId ?? Guid.NewGuid(),
            new Uri("https://example.com/webhooks"),
            ["secret"],
            ["TestEvent"],
            true);

    [Fact]
    public async Task OnAttemptAsync_RecordsAttempt_NeverThrows()
    {
        var observer = new InMemoryWebhookDeliveryObserver();
        var subscription = CreateSubscription();

        await observer.OnAttemptAsync(subscription, 1, CancellationToken.None);

        Assert.Single(observer.Attempts);
        Assert.Equal((subscription, 1), observer.Attempts[0]);
        observer.ShouldHaveObservedAttempt(subscription.SubscriptionId);
    }

    [Fact]
    public async Task OnAttemptAsync_NullSubscription_Throws() =>
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await new InMemoryWebhookDeliveryObserver().OnAttemptAsync(null!, 1, CancellationToken.None));

    [Fact]
    public void ShouldHaveObservedAttempt_NoMatch_Throws() =>
        Assert.Throws<InvalidOperationException>(
            () => new InMemoryWebhookDeliveryObserver().ShouldHaveObservedAttempt(Guid.NewGuid()));

    [Fact]
    public async Task OnCompletedAsync_RecordsCompletion_NeverThrows()
    {
        var observer = new InMemoryWebhookDeliveryObserver();
        var subscription = CreateSubscription();
        var result = new WebhookDeliveryResult(subscription.SubscriptionId, Guid.NewGuid(), true, 200, 1, null);

        await observer.OnCompletedAsync(subscription, result, CancellationToken.None);

        Assert.Single(observer.Completions);
        Assert.Equal((subscription, result), observer.Completions[0]);
        Assert.Equal(result, observer.ShouldHaveObservedCompletion(subscription.SubscriptionId));
    }

    [Fact]
    public async Task OnCompletedAsync_NullArguments_Throw()
    {
        var observer = new InMemoryWebhookDeliveryObserver();
        var subscription = CreateSubscription();
        var result = new WebhookDeliveryResult(subscription.SubscriptionId, Guid.NewGuid(), true, 200, 1, null);

        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await observer.OnCompletedAsync(null!, result, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await observer.OnCompletedAsync(subscription, null!, CancellationToken.None));
    }

    [Fact]
    public void ShouldHaveObservedCompletion_NoMatch_Throws() =>
        Assert.Throws<InvalidOperationException>(
            () => new InMemoryWebhookDeliveryObserver().ShouldHaveObservedCompletion(Guid.NewGuid()));
}

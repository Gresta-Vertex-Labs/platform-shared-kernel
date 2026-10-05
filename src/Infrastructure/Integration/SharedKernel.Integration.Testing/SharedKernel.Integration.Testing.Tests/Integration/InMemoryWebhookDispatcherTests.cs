using SharedKernel.Integration.Webhooks.Dispatch;
using SharedKernel.Integration.Webhooks.Subscriptions;
using SharedKernel.Testing.Integration;
using Xunit;

namespace SharedKernel.Testing.SelfTests.Integration;

/// <summary>
/// Proves <see cref="InMemoryWebhookDispatcher"/> against <c>IWebhookDispatcher</c>'s documented
/// contract — no consuming domain has adopted this fake yet (see <c>src/Testing/state-map.md</c>
/// T-86), so this self-test is the only behavioral proof today, per the SelfTests routing rule.
/// </summary>
public sealed class InMemoryWebhookDispatcherTests
{
    private static WebhookSubscription CreateSubscription(Guid? subscriptionId = null) =>
        new(
            subscriptionId ?? Guid.NewGuid(),
            new Uri("https://example.com/webhooks"),
            ["secret"],
            ["TestEvent"],
            true);

    [Fact]
    public async Task DispatchAsync_RecordsEvent_ReturnsEmptyList_WhenUnconfigured()
    {
        var dispatcher = new InMemoryWebhookDispatcher();
        var evt = new TestIntegrationEvent(Guid.NewGuid());

        var result = await dispatcher.DispatchAsync(evt, CancellationToken.None);

        Assert.Empty(result);
        Assert.Same(evt, dispatcher.ShouldHaveDispatched<TestIntegrationEvent>());
    }

    [Fact]
    public async Task DispatchAsync_SetDispatchResult_OverridesDefaultEmptyList()
    {
        var dispatcher = new InMemoryWebhookDispatcher();
        var evt = new TestIntegrationEvent(Guid.NewGuid());
        var expected = new WebhookDeliveryResult(Guid.NewGuid(), Guid.NewGuid(), true, 200, 1, null);
        dispatcher.SetDispatchResult<TestIntegrationEvent>(_ => [expected]);

        var result = await dispatcher.DispatchAsync(evt, CancellationToken.None);

        Assert.Single(result);
        Assert.Equal(expected, result[0]);
    }

    [Fact]
    public async Task DispatchAsync_NullEvent_Throws() =>
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await new InMemoryWebhookDispatcher().DispatchAsync<TestIntegrationEvent>(null!, CancellationToken.None));

    [Fact]
    public void ShouldHaveDispatched_NoMatch_Throws() =>
        Assert.Throws<InvalidOperationException>(() => new InMemoryWebhookDispatcher().ShouldHaveDispatched<TestIntegrationEvent>());

    [Fact]
    public async Task ShouldNotHaveDispatched_NoMatch_DoesNotThrow()
    {
        var dispatcher = new InMemoryWebhookDispatcher();
        await dispatcher.DispatchAsync(new OtherIntegrationEvent(Guid.NewGuid()), CancellationToken.None);

        dispatcher.ShouldNotHaveDispatched<TestIntegrationEvent>();
    }

    [Fact]
    public async Task ShouldNotHaveDispatched_Match_Throws()
    {
        var dispatcher = new InMemoryWebhookDispatcher();
        await dispatcher.DispatchAsync(new TestIntegrationEvent(Guid.NewGuid()), CancellationToken.None);

        Assert.Throws<InvalidOperationException>(() => dispatcher.ShouldNotHaveDispatched<TestIntegrationEvent>());
    }

    [Fact]
    public async Task DispatchToSubscriptionAsync_RecordsPair_ReturnsSyntheticSuccess_WhenUnconfigured()
    {
        var dispatcher = new InMemoryWebhookDispatcher();
        var subscription = CreateSubscription();
        var evt = new TestIntegrationEvent(Guid.NewGuid());

        var result = await dispatcher.DispatchToSubscriptionAsync(subscription, evt, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(subscription.SubscriptionId, result.SubscriptionId);
        Assert.Equal(200, result.StatusCode);
        Assert.Equal(1, result.Attempts);
        Assert.Null(result.Error);
        Assert.NotEqual(Guid.Empty, result.DeliveryId);
        dispatcher.ShouldHaveDispatchedTo(subscription.SubscriptionId);
        Assert.Equal((subscription.SubscriptionId, (object)evt), dispatcher.DispatchedTo[0]);
    }

    [Fact]
    public async Task DispatchToSubscriptionAsync_SetDispatchResult_OverridesDefaultSuccess()
    {
        var dispatcher = new InMemoryWebhookDispatcher();
        var subscription = CreateSubscription();
        var failure = new WebhookDeliveryResult(subscription.SubscriptionId, Guid.NewGuid(), false, 500, 5, "boom");
        dispatcher.SetDispatchResult(subscription.SubscriptionId, failure);

        var result = await dispatcher.DispatchToSubscriptionAsync(
            subscription, new TestIntegrationEvent(Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(failure, result);
    }

    [Fact]
    public async Task DispatchToSubscriptionAsync_NullArguments_Throw()
    {
        var dispatcher = new InMemoryWebhookDispatcher();
        var subscription = CreateSubscription();

        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await dispatcher.DispatchToSubscriptionAsync<TestIntegrationEvent>(null!, new TestIntegrationEvent(Guid.NewGuid()), CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await dispatcher.DispatchToSubscriptionAsync<TestIntegrationEvent>(subscription, null!, CancellationToken.None));
    }

    [Fact]
    public void ShouldHaveDispatchedTo_NoMatch_Throws() =>
        Assert.Throws<InvalidOperationException>(() => new InMemoryWebhookDispatcher().ShouldHaveDispatchedTo(Guid.NewGuid()));

    [Fact]
    public async Task SendTestDeliveryAsync_RecordsSubscription_ReturnsSyntheticSuccess_WhenUnconfigured()
    {
        var dispatcher = new InMemoryWebhookDispatcher();
        var subscription = CreateSubscription();

        var result = await dispatcher.SendTestDeliveryAsync(subscription, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(subscription.SubscriptionId, result.SubscriptionId);
        Assert.Same(subscription, dispatcher.TestDeliveries[0]);
        dispatcher.ShouldHaveSentTestDelivery(subscription.SubscriptionId);
    }

    [Fact]
    public async Task SendTestDeliveryAsync_SetTestDeliveryResult_OverridesDefaultSuccess()
    {
        var dispatcher = new InMemoryWebhookDispatcher();
        var subscription = CreateSubscription();
        var failure = new WebhookDeliveryResult(subscription.SubscriptionId, Guid.NewGuid(), false, null, 3, "timeout");
        dispatcher.SetTestDeliveryResult(subscription.SubscriptionId, failure);

        var result = await dispatcher.SendTestDeliveryAsync(subscription, CancellationToken.None);

        Assert.Equal(failure, result);
    }

    [Fact]
    public async Task SendTestDeliveryAsync_NullSubscription_Throws() =>
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await new InMemoryWebhookDispatcher().SendTestDeliveryAsync(null!, CancellationToken.None));

    [Fact]
    public void ShouldHaveSentTestDelivery_NoMatch_Throws() =>
        Assert.Throws<InvalidOperationException>(() => new InMemoryWebhookDispatcher().ShouldHaveSentTestDelivery(Guid.NewGuid()));

    [Fact]
    public async Task SendTestDeliveryAsync_DoesNotAffectDispatchedTo()
    {
        var dispatcher = new InMemoryWebhookDispatcher();
        var subscription = CreateSubscription();

        await dispatcher.SendTestDeliveryAsync(subscription, CancellationToken.None);

        Assert.Empty(dispatcher.DispatchedTo);
        Assert.Throws<InvalidOperationException>(() => dispatcher.ShouldHaveDispatchedTo(subscription.SubscriptionId));
    }

    [SharedKernel.Contracts.Events.IntegrationEvent("tests.testing.integration.webhook-dispatcher-event")]
    private sealed record TestIntegrationEvent(Guid EventId) : SharedKernel.Contracts.Events.IIntegrationEvent
    {
        public DateTimeOffset OccurredOn { get; } = DateTimeOffset.UnixEpoch;
    }

    [SharedKernel.Contracts.Events.IntegrationEvent("tests.testing.integration.webhook-dispatcher-other-event")]
    private sealed record OtherIntegrationEvent(Guid EventId) : SharedKernel.Contracts.Events.IIntegrationEvent
    {
        public DateTimeOffset OccurredOn { get; } = DateTimeOffset.UnixEpoch;
    }
}

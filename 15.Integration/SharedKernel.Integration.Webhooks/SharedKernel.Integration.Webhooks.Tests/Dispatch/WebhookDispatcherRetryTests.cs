using System.Net;
using FluentAssertions;
using SharedKernel.Integration.Webhooks.Events;
using SharedKernel.Integration.Webhooks.Subscriptions;
using SharedKernel.Integration.Webhooks.Tests.TestSupport;

namespace SharedKernel.Integration.Webhooks.Tests.Dispatch;

public sealed class WebhookDispatcherRetryTests
{
    private static WebhookSubscription Subscription() =>
        new(Guid.NewGuid(), new Uri("https://example.test/hook"), "secret", [], true);

    [Fact]
    public async Task DispatchToSubscriptionAsync_TransientFailureThenSuccess_RetriesAndReportsAttempts()
    {
        var subscription = Subscription();
        var store = new FakeWebhookSubscriptionStore([subscription]);

        using var handler = new StubHttpMessageHandler((_, callNumber) =>
            callNumber < 3
                ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                : new HttpResponseMessage(HttpStatusCode.OK));

        using var harness = new WebhookTestHarness(handler, store, o =>
        {
            o.MaxAttempts = 5;
            o.BaseBackoffDelay = TimeSpan.FromMilliseconds(1);
            o.MaxBackoffDelay = TimeSpan.FromMilliseconds(5);
            o.RequestTimeout = TimeSpan.FromSeconds(5);
        });

        var result = await harness.Dispatcher.DispatchToSubscriptionAsync(
            subscription,
            new TestOrderShippedEvent(Guid.NewGuid(), DateTimeOffset.UtcNow, Guid.NewGuid()),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Attempts.Should().Be(3);
        result.StatusCode.Should().Be((int)HttpStatusCode.OK);
    }

    [Fact]
    public async Task DispatchToSubscriptionAsync_ExhaustsMaxAttempts_ReturnsFailureWithoutThrowing()
    {
        var subscription = Subscription();
        var store = new FakeWebhookSubscriptionStore([subscription]);

        using var handler = new StubHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        using var harness = new WebhookTestHarness(handler, store, o =>
        {
            o.MaxAttempts = 3;
            o.BaseBackoffDelay = TimeSpan.FromMilliseconds(1);
            o.MaxBackoffDelay = TimeSpan.FromMilliseconds(5);
        });

        var result = await harness.Dispatcher.DispatchToSubscriptionAsync(
            subscription,
            new TestOrderShippedEvent(Guid.NewGuid(), DateTimeOffset.UtcNow, Guid.NewGuid()),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().NotBeNullOrEmpty();
        result.StatusCode.Should().Be((int)HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task DispatchToSubscriptionAsync_ExhaustedDelivery_PublishesExactlyOneExhaustedEvent()
    {
        var subscription = Subscription();
        var store = new FakeWebhookSubscriptionStore([subscription]);

        using var handler = new StubHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        using var harness = new WebhookTestHarness(handler, store, o =>
        {
            o.MaxAttempts = 2;
            o.BaseBackoffDelay = TimeSpan.FromMilliseconds(1);
            o.MaxBackoffDelay = TimeSpan.FromMilliseconds(5);
        });

        var integrationEvent = new TestOrderShippedEvent(Guid.NewGuid(), DateTimeOffset.UtcNow, Guid.NewGuid());

        await harness.Dispatcher.DispatchToSubscriptionAsync(subscription, integrationEvent, CancellationToken.None);

        var exhausted = harness.EventPublisher.ShouldHavePublishedOnce<WebhookDeliveryExhaustedEvent>();
        exhausted.SubscriptionId.Should().Be(subscription.SubscriptionId);
        exhausted.EventType.Should().Be(nameof(TestOrderShippedEvent));
    }

    [Fact]
    public async Task DispatchToSubscriptionAsync_SuccessfulDelivery_DoesNotPublishExhaustedEvent()
    {
        var subscription = Subscription();
        var store = new FakeWebhookSubscriptionStore([subscription]);

        using var handler = new StubHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK));

        using var harness = new WebhookTestHarness(handler, store, o => o.MaxAttempts = 1);

        await harness.Dispatcher.DispatchToSubscriptionAsync(
            subscription,
            new TestOrderShippedEvent(Guid.NewGuid(), DateTimeOffset.UtcNow, Guid.NewGuid()),
            CancellationToken.None);

        harness.EventPublisher.ShouldNotHavePublished<WebhookDeliveryExhaustedEvent>();
    }

    [Fact]
    public async Task DispatchToSubscriptionAsync_TransportException_ReturnsFailureWithNullStatusCode()
    {
        var subscription = Subscription();
        var store = new FakeWebhookSubscriptionStore([subscription]);

        using var handler = new StubHttpMessageHandler((_, _) => throw new HttpRequestException("connection refused"));

        using var harness = new WebhookTestHarness(handler, store, o =>
        {
            o.MaxAttempts = 1;
        });

        var result = await harness.Dispatcher.DispatchToSubscriptionAsync(
            subscription,
            new TestOrderShippedEvent(Guid.NewGuid(), DateTimeOffset.UtcNow, Guid.NewGuid()),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().BeNull();
        result.Error.Should().NotBeNullOrEmpty();
    }
}

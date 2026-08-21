using System.Net;
using FluentAssertions;
using SharedKernel.Integration.Webhooks.Signing;
using SharedKernel.Integration.Webhooks.Subscriptions;
using SharedKernel.Integration.Webhooks.Tests.TestSupport;

namespace SharedKernel.Integration.Webhooks.Tests.Dispatch;

/// <summary>
/// Coverage for P-423: the per-delivery <c>X-Webhook-Delivery-Id</c> header stays identical across
/// every retry attempt of one delivery, and matches the returned
/// <see cref="Dispatch.WebhookDeliveryResult.DeliveryId"/>.
/// </summary>
public sealed class WebhookDeliveryIdTests
{
    private static WebhookSubscription Subscription() =>
        new(Guid.NewGuid(), new Uri("https://example.test/hook"), ["secret"], [], true);

    [Fact]
    public async Task DispatchToSubscriptionAsync_MultipleRetries_DeliveryIdHeaderStableAcrossAllAttempts()
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
        });

        var result = await harness.Dispatcher.DispatchToSubscriptionAsync(
            subscription,
            new TestOrderShippedEvent(Guid.NewGuid(), DateTimeOffset.UtcNow, Guid.NewGuid()),
            CancellationToken.None);

        result.Attempts.Should().Be(3);
        handler.Requests.Should().HaveCount(3);

        var deliveryIdHeaderValues = handler.Requests
            .Select(r => r.Headers.GetValues(WebhookSignatureHeaders.DeliveryIdHeaderName).Single())
            .Distinct()
            .ToList();

        deliveryIdHeaderValues.Should().ContainSingle();
        deliveryIdHeaderValues[0].Should().Be(result.DeliveryId.ToString());
    }

    [Fact]
    public async Task DispatchToSubscriptionAsync_SuccessfulDelivery_ResultDeliveryIdIsNonEmpty()
    {
        var subscription = Subscription();
        var store = new FakeWebhookSubscriptionStore([subscription]);

        using var handler = new StubHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK));
        using var harness = new WebhookTestHarness(handler, store, o => o.MaxAttempts = 1);

        var result = await harness.Dispatcher.DispatchToSubscriptionAsync(
            subscription,
            new TestOrderShippedEvent(Guid.NewGuid(), DateTimeOffset.UtcNow, Guid.NewGuid()),
            CancellationToken.None);

        result.DeliveryId.Should().NotBe(Guid.Empty);
        handler.Requests[0].Headers.GetValues(WebhookSignatureHeaders.DeliveryIdHeaderName).Single()
            .Should().Be(result.DeliveryId.ToString());
    }

    [Fact]
    public async Task DispatchToSubscriptionAsync_TwoSeparateDeliveries_HaveDistinctDeliveryIds()
    {
        var subscription = Subscription();
        var store = new FakeWebhookSubscriptionStore([subscription]);

        using var handler = new StubHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK));
        using var harness = new WebhookTestHarness(handler, store, o => o.MaxAttempts = 1);

        var first = await harness.Dispatcher.DispatchToSubscriptionAsync(
            subscription,
            new TestOrderShippedEvent(Guid.NewGuid(), DateTimeOffset.UtcNow, Guid.NewGuid()),
            CancellationToken.None);

        var second = await harness.Dispatcher.DispatchToSubscriptionAsync(
            subscription,
            new TestOrderShippedEvent(Guid.NewGuid(), DateTimeOffset.UtcNow, Guid.NewGuid()),
            CancellationToken.None);

        first.DeliveryId.Should().NotBe(second.DeliveryId);
    }
}

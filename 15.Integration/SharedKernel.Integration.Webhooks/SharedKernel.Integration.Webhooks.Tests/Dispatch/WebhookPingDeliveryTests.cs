using System.Diagnostics;
using System.Net;
using System.Text.Json;
using FluentAssertions;
using SharedKernel.Contracts.Events;
using SharedKernel.Integration.Webhooks.Dispatch;
using SharedKernel.Integration.Webhooks.Events;
using SharedKernel.Integration.Webhooks.Signing;
using SharedKernel.Integration.Webhooks.Subscriptions;
using SharedKernel.Integration.Webhooks.Tests.TestSupport;

namespace SharedKernel.Integration.Webhooks.Tests.Dispatch;

/// <summary>
/// Coverage for P-429: <c>SendTestDeliveryAsync</c> reuses the real delivery pipeline verbatim — no
/// parallel signing/retry/observer logic — and the delivered event type is unambiguously
/// <c>"sharedkernel.webhooks.ping"</c>.
/// </summary>
public sealed class WebhookPingDeliveryTests
{
    private static WebhookSubscription Subscription(string secret = "ping-secret") =>
        new(Guid.NewGuid(), new Uri("https://example.test/hook"), [secret], [], true);

    [Fact]
    public async Task SendTestDeliveryAsync_DeliversSignedPingWithCorrectEventType()
    {
        const string secret = "ping-secret";
        var subscription = Subscription(secret);
        var store = new FakeWebhookSubscriptionStore([subscription]);

        string? body = null;
        string? timestamp = null;
        string? signature = null;

        using var handler = new StubHttpMessageHandler((request, _) =>
        {
            // Captured synchronously within the responder — the dispatcher disposes its
            // HttpRequestMessage (and StringContent) once SendAsync returns, so reading
            // request.Content after the dispatch call completes would throw ObjectDisposedException.
            body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            timestamp = request.Headers.GetValues(WebhookSignatureHeaders.TimestampHeaderName).Single();
            signature = request.Headers.GetValues(WebhookSignatureHeaders.SignatureHeaderName).Single();
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var harness = new WebhookTestHarness(handler, store, o => o.MaxAttempts = 1);

        var result = await harness.Dispatcher.SendTestDeliveryAsync(subscription, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        body.Should().NotBeNull();

        WebhookSignatureVerifier.Verify(body, timestamp, signature, secret).Should().BeTrue();

        using var document = JsonDocument.Parse(body!);
        document.RootElement.TryGetProperty("EventId", out _).Should().BeTrue();
    }

    [Fact]
    public void WebhookPingEvent_DeclaresReservedIntegrationEventName()
    {
        WebhookPingEvent.EventName.Should().Be("sharedkernel.webhooks.ping");
        IntegrationEventDescriptor.For<WebhookPingEvent>().Name.Should().Be(WebhookPingEvent.EventName);
        IntegrationEventDescriptor.For<WebhookPingEvent>().Version.Should().Be(1);
    }

    [Fact]
    public void WebhookDeliveryExhaustedEvent_DeclaresIntegrationEventName()
    {
        WebhookDeliveryExhaustedEvent.EventName.Should().Be("sharedkernel.webhooks.delivery-exhausted");
        IntegrationEventDescriptor.For<WebhookDeliveryExhaustedEvent>().Name.Should().Be(WebhookDeliveryExhaustedEvent.EventName);
    }

    [Fact]
    public async Task SendTestDeliveryAsync_TagsSpanWithPingEventName()
    {
        var subscription = Subscription();
        var store = new FakeWebhookSubscriptionStore([subscription]);
        var eventTypeTags = new System.Collections.Concurrent.ConcurrentBag<object?>();

        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == WebhookIntegrationActivitySource.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                if (Equals(activity.GetTagItem(WebhookActivityTags.SubscriptionId), subscription.SubscriptionId))
                    eventTypeTags.Add(activity.GetTagItem(WebhookActivityTags.EventType));
            },
        };
        ActivitySource.AddActivityListener(listener);

        using var handler = new StubHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK));
        using var harness = new WebhookTestHarness(handler, store, o => o.MaxAttempts = 1);

        var result = await harness.Dispatcher.SendTestDeliveryAsync(subscription, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        eventTypeTags.Should().ContainSingle().Which.Should().Be(WebhookPingEvent.EventName);
    }

    [Fact]
    public async Task SendTestDeliveryAsync_TransientFailureThenSuccess_RetriesIdenticallyToRealDispatch()
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

        var result = await harness.Dispatcher.SendTestDeliveryAsync(subscription, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Attempts.Should().Be(3);
    }

    [Fact]
    public async Task SendTestDeliveryAsync_IncludesDeliveryIdHeader_StableAcrossRetries()
    {
        var subscription = Subscription();
        var store = new FakeWebhookSubscriptionStore([subscription]);

        using var handler = new StubHttpMessageHandler((_, callNumber) =>
            callNumber < 2
                ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                : new HttpResponseMessage(HttpStatusCode.OK));

        using var harness = new WebhookTestHarness(handler, store, o =>
        {
            o.MaxAttempts = 3;
            o.BaseBackoffDelay = TimeSpan.FromMilliseconds(1);
            o.MaxBackoffDelay = TimeSpan.FromMilliseconds(5);
        });

        var result = await harness.Dispatcher.SendTestDeliveryAsync(subscription, CancellationToken.None);

        var deliveryIds = handler.Requests
            .Select(r => r.Headers.GetValues(WebhookSignatureHeaders.DeliveryIdHeaderName).Single())
            .Distinct()
            .ToList();

        deliveryIds.Should().ContainSingle();
        deliveryIds[0].Should().Be(result.DeliveryId.ToString());
    }

    [Fact]
    public async Task SendTestDeliveryAsync_NullSubscription_Throws()
    {
        var store = new FakeWebhookSubscriptionStore([]);
        using var handler = new StubHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK));
        using var harness = new WebhookTestHarness(handler, store);

        var act = async () => await harness.Dispatcher.SendTestDeliveryAsync(null!, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }
}

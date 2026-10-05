using System.Net;
using FluentAssertions;
using SharedKernel.Integration.Webhooks.Signing;
using SharedKernel.Integration.Webhooks.Subscriptions;
using SharedKernel.Integration.Webhooks.Tests.TestSupport;

namespace SharedKernel.Integration.Webhooks.Tests.Dispatch;

/// <summary>
/// Coverage for P-426: optional static per-subscription headers are applied to every outbound
/// delivery, and a reserved-header-name collision is rejected before any HTTP call.
/// </summary>
public sealed class WebhookCustomHeadersTests
{
    private static WebhookSubscription Subscription(IReadOnlyDictionary<string, string>? headers) =>
        new(Guid.NewGuid(), new Uri("https://example.test/hook"), ["secret"], [], true, headers);

    [Fact]
    public async Task DispatchToSubscriptionAsync_SubscriptionHeaders_ArePresentOnOutboundRequest()
    {
        var headers = new Dictionary<string, string>
        {
            ["X-Partner-Id"] = "partner-123",
            ["X-Custom-Routing"] = "region-eu",
        };
        var subscription = Subscription(headers);
        var store = new FakeWebhookSubscriptionStore([subscription]);

        using var handler = new StubHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK));
        using var harness = new WebhookTestHarness(handler, store, o => o.MaxAttempts = 1);

        var result = await harness.Dispatcher.DispatchToSubscriptionAsync(
            subscription,
            new TestOrderShippedEvent(Guid.NewGuid(), DateTimeOffset.UtcNow, Guid.NewGuid()),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var request = handler.Requests.Single();
        request.Headers.GetValues("X-Partner-Id").Should().ContainSingle("partner-123");
        request.Headers.GetValues("X-Custom-Routing").Should().ContainSingle("region-eu");
    }

    [Theory]
    [InlineData(WebhookSignatureHeaders.SignatureHeaderName)]
    [InlineData(WebhookSignatureHeaders.TimestampHeaderName)]
    [InlineData(WebhookSignatureHeaders.DeliveryIdHeaderName)]
    [InlineData("x-webhook-signature")] // case-insensitive collision
    public async Task DispatchToSubscriptionAsync_HeaderCollidesWithReservedName_RejectedBeforeAnyHttpCall(string collidingName)
    {
        var headers = new Dictionary<string, string> { [collidingName] = "attacker-supplied-value" };
        var subscription = Subscription(headers);
        var store = new FakeWebhookSubscriptionStore([subscription]);

        using var handler = new StubHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK));
        using var harness = new WebhookTestHarness(handler, store, o => o.MaxAttempts = 1);

        var result = await harness.Dispatcher.DispatchToSubscriptionAsync(
            subscription,
            new TestOrderShippedEvent(Guid.NewGuid(), DateTimeOffset.UtcNow, Guid.NewGuid()),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().NotBeNullOrEmpty();
        handler.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task DispatchToSubscriptionAsync_NoHeadersConfigured_DeliveryUnaffected()
    {
        var subscription = Subscription(headers: null);
        var store = new FakeWebhookSubscriptionStore([subscription]);

        using var handler = new StubHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK));
        using var harness = new WebhookTestHarness(handler, store, o => o.MaxAttempts = 1);

        var result = await harness.Dispatcher.DispatchToSubscriptionAsync(
            subscription,
            new TestOrderShippedEvent(Guid.NewGuid(), DateTimeOffset.UtcNow, Guid.NewGuid()),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }
}

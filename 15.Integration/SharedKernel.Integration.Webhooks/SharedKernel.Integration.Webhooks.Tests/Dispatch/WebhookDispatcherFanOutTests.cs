using System.Net;
using FluentAssertions;
using SharedKernel.Integration.Webhooks.Subscriptions;
using SharedKernel.Integration.Webhooks.Tests.TestSupport;

namespace SharedKernel.Integration.Webhooks.Tests.Dispatch;

public sealed class WebhookDispatcherFanOutTests
{
    private static WebhookSubscription Subscription(bool isActive = true, params string[] eventTypes) =>
        new(Guid.NewGuid(), new Uri("https://example.test/hook"), "secret", eventTypes, isActive);

    [Fact]
    public async Task DispatchAsync_FansOutToAllActiveMatchingSubscriptions()
    {
        var active1 = Subscription();
        var active2 = Subscription();
        var store = new FakeWebhookSubscriptionStore([active1, active2]);
        using var handler = new StubHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK));
        using var harness = new WebhookTestHarness(handler, store, o => o.MaxAttempts = 1);

        var results = await harness.Dispatcher.DispatchAsync(
            new TestOrderShippedEvent(Guid.NewGuid(), DateTimeOffset.UtcNow, Guid.NewGuid()), CancellationToken.None);

        results.Should().HaveCount(2);
        results.Should().OnlyContain(r => r.IsSuccess);
        results.Select(r => r.SubscriptionId).Should().BeEquivalentTo([active1.SubscriptionId, active2.SubscriptionId]);
    }

    [Fact]
    public async Task DispatchAsync_ExcludesInactiveSubscriptions()
    {
        var active = Subscription();
        var inactive = Subscription(isActive: false);
        var store = new FakeWebhookSubscriptionStore([active, inactive]);
        using var handler = new StubHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK));
        using var harness = new WebhookTestHarness(handler, store, o => o.MaxAttempts = 1);

        var results = await harness.Dispatcher.DispatchAsync(
            new TestOrderShippedEvent(Guid.NewGuid(), DateTimeOffset.UtcNow, Guid.NewGuid()), CancellationToken.None);

        results.Should().ContainSingle();
        results[0].SubscriptionId.Should().Be(active.SubscriptionId);
    }

    [Fact]
    public async Task DispatchAsync_ExcludesNonMatchingEventTypeSubscriptions()
    {
        var matching = Subscription(eventTypes: nameof(TestOrderShippedEvent));
        var nonMatching = Subscription(eventTypes: "SomeOtherEvent");
        var store = new FakeWebhookSubscriptionStore([matching, nonMatching]);
        using var handler = new StubHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK));
        using var harness = new WebhookTestHarness(handler, store, o => o.MaxAttempts = 1);

        var results = await harness.Dispatcher.DispatchAsync(
            new TestOrderShippedEvent(Guid.NewGuid(), DateTimeOffset.UtcNow, Guid.NewGuid()), CancellationToken.None);

        results.Should().ContainSingle();
        results[0].SubscriptionId.Should().Be(matching.SubscriptionId);
    }

    [Fact]
    public async Task DispatchAsync_EmptyEventTypes_TreatedAsSubscribedToEverything()
    {
        var subscription = Subscription();
        var store = new FakeWebhookSubscriptionStore([subscription]);
        using var handler = new StubHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK));
        using var harness = new WebhookTestHarness(handler, store, o => o.MaxAttempts = 1);

        var results = await harness.Dispatcher.DispatchAsync(
            new TestOrderShippedEvent(Guid.NewGuid(), DateTimeOffset.UtcNow, Guid.NewGuid()), CancellationToken.None);

        results.Should().ContainSingle();
    }

    [Fact]
    public async Task DispatchAsync_OneSubscriptionFailing_DoesNotAffectOthers()
    {
        // Distinct URLs per subscription so the stub handler can route failure deterministically.
        var failingSub = Subscription() with { Url = new Uri("https://example.test/fail") };
        var succeedingSub = Subscription() with { Url = new Uri("https://example.test/succeed") };
        var store = new FakeWebhookSubscriptionStore([failingSub, succeedingSub]);

        using var handler = new StubHttpMessageHandler((request, _) =>
            request.RequestUri!.AbsolutePath == "/fail"
                ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                : new HttpResponseMessage(HttpStatusCode.OK));

        using var harness = new WebhookTestHarness(handler, store, o => o.MaxAttempts = 1);

        var results = await harness.Dispatcher.DispatchAsync(
            new TestOrderShippedEvent(Guid.NewGuid(), DateTimeOffset.UtcNow, Guid.NewGuid()), CancellationToken.None);

        results.Should().HaveCount(2);
        results.Single(r => r.SubscriptionId == failingSub.SubscriptionId).IsSuccess.Should().BeFalse();
        results.Single(r => r.SubscriptionId == succeedingSub.SubscriptionId).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task DispatchAsync_NoMatchingSubscriptions_ReturnsEmptyResult()
    {
        var store = new FakeWebhookSubscriptionStore([]);
        using var handler = new StubHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK));
        using var harness = new WebhookTestHarness(handler, store, o => o.MaxAttempts = 1);

        var results = await harness.Dispatcher.DispatchAsync(
            new TestOrderShippedEvent(Guid.NewGuid(), DateTimeOffset.UtcNow, Guid.NewGuid()), CancellationToken.None);

        results.Should().BeEmpty();
        handler.CallCount.Should().Be(0);
    }
}

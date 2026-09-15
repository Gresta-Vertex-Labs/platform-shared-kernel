using System.Net;
using FluentAssertions;
using SharedKernel.Contracts.Events;
using SharedKernel.Integration.Webhooks.Subscriptions;
using SharedKernel.Integration.Webhooks.Tests.TestSupport;

namespace SharedKernel.Integration.Webhooks.Tests.Dispatch;

public sealed class WebhookDispatcherFanOutTests
{
    private static WebhookSubscription Subscription(bool isActive = true, params string[] eventTypes) =>
        new(Guid.NewGuid(), new Uri("https://example.test/hook"), ["secret"], eventTypes, isActive);

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
        var matching = Subscription(eventTypes: TestOrderShippedEvent.EventName);
        var nonMatching = Subscription(eventTypes: "tests.webhooks.some-other-event");
        var store = new FakeWebhookSubscriptionStore([matching, nonMatching]);
        using var handler = new StubHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK));
        using var harness = new WebhookTestHarness(handler, store, o => o.MaxAttempts = 1);

        var results = await harness.Dispatcher.DispatchAsync(
            new TestOrderShippedEvent(Guid.NewGuid(), DateTimeOffset.UtcNow, Guid.NewGuid()), CancellationToken.None);

        results.Should().ContainSingle();
        results[0].SubscriptionId.Should().Be(matching.SubscriptionId);
    }

    [Fact]
    public async Task DispatchAsync_SubscriptionKeyedByClassName_DoesNotMatch()
    {
        // The routing key is the [IntegrationEvent] name; a subscription still keyed by the CLR class
        // name must not receive the event.
        var byClassName = Subscription(eventTypes: nameof(TestOrderShippedEvent));
        var store = new FakeWebhookSubscriptionStore([byClassName]);
        using var handler = new StubHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK));
        using var harness = new WebhookTestHarness(handler, store, o => o.MaxAttempts = 1);

        var results = await harness.Dispatcher.DispatchAsync(
            new TestOrderShippedEvent(Guid.NewGuid(), DateTimeOffset.UtcNow, Guid.NewGuid()), CancellationToken.None);

        results.Should().BeEmpty();
        handler.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task DispatchAsync_AsInterfaceTypedEvent_RoutesByRuntimeTypeName()
    {
        // TEvent is IIntegrationEvent here; the routing key must still come from the runtime type.
        var matching = Subscription(eventTypes: TestOrderShippedEvent.EventName);
        var store = new FakeWebhookSubscriptionStore([matching]);
        using var handler = new StubHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK));
        using var harness = new WebhookTestHarness(handler, store, o => o.MaxAttempts = 1);

        IIntegrationEvent integrationEvent = new TestOrderShippedEvent(Guid.NewGuid(), DateTimeOffset.UtcNow, Guid.NewGuid());
        var results = await harness.Dispatcher.DispatchAsync(integrationEvent, CancellationToken.None);

        results.Should().ContainSingle();
        results[0].SubscriptionId.Should().Be(matching.SubscriptionId);
    }

    [Fact]
    public async Task DispatchAsync_EventWithoutIntegrationEventAttribute_Throws()
    {
        var store = new FakeWebhookSubscriptionStore([Subscription()]);
        using var handler = new StubHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK));
        using var harness = new WebhookTestHarness(handler, store, o => o.MaxAttempts = 1);

        var act = async () => await harness.Dispatcher.DispatchAsync(
            new UndeclaredWebhookEvent(Guid.NewGuid(), DateTimeOffset.UtcNow), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        handler.CallCount.Should().Be(0);
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

// Deliberately has no [IntegrationEvent] attribute.
file sealed record UndeclaredWebhookEvent(Guid EventId, DateTimeOffset OccurredOn) : IIntegrationEvent;

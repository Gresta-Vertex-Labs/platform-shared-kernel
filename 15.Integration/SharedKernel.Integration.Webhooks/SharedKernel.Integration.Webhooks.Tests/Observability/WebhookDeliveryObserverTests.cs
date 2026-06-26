using System.Net;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Integration.Webhooks.Dispatch;
using SharedKernel.Integration.Webhooks.Extensions;
using SharedKernel.Integration.Webhooks.Observability;
using SharedKernel.Integration.Webhooks.Subscriptions;
using SharedKernel.Integration.Webhooks.Tests.TestSupport;

namespace SharedKernel.Integration.Webhooks.Tests.Observability;

public sealed class WebhookDeliveryObserverTests
{
    private sealed class RecordingObserver : IWebhookDeliveryObserver
    {
        public int AttemptCalls { get; private set; }

        public int CompletedCalls { get; private set; }

        public Task OnAttemptAsync(WebhookSubscription subscription, int attemptNumber, CancellationToken ct)
        {
            AttemptCalls++;
            return Task.CompletedTask;
        }

        public Task OnCompletedAsync(WebhookSubscription subscription, WebhookDeliveryResult result, CancellationToken ct)
        {
            CompletedCalls++;
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingObserver : IWebhookDeliveryObserver
    {
        public Task OnAttemptAsync(WebhookSubscription subscription, int attemptNumber, CancellationToken ct) =>
            throw new InvalidOperationException("observer attempt failure");

        public Task OnCompletedAsync(WebhookSubscription subscription, WebhookDeliveryResult result, CancellationToken ct) =>
            throw new InvalidOperationException("observer completion failure");
    }

    /// <summary>Records calls via a static counter so a type-registered (not instance-registered) observer is still assertable.</summary>
    private sealed class StaticCountingObserver : IWebhookDeliveryObserver
    {
        public static int AttemptCalls;
        public static int CompletedCalls;

        public Task OnAttemptAsync(WebhookSubscription subscription, int attemptNumber, CancellationToken ct)
        {
            Interlocked.Increment(ref AttemptCalls);
            return Task.CompletedTask;
        }

        public Task OnCompletedAsync(WebhookSubscription subscription, WebhookDeliveryResult result, CancellationToken ct)
        {
            Interlocked.Increment(ref CompletedCalls);
            return Task.CompletedTask;
        }
    }

    private static WebhookSubscription Subscription() =>
        new(Guid.NewGuid(), new Uri("https://example.test/hook"), "secret", [], true);

    [Fact]
    public async Task DispatchToSubscriptionAsync_InvokesRegisteredObserver_OnceForAttemptAndCompletion()
    {
        var subscription = Subscription();
        var store = new FakeWebhookSubscriptionStore([subscription]);
        var observer = new RecordingObserver();

        using var handler = new StubHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK));
        using var harness = new WebhookTestHarness(
            handler,
            store,
            o => o.MaxAttempts = 1,
            services => services.AddSingleton<IWebhookDeliveryObserver>(observer));

        await harness.Dispatcher.DispatchToSubscriptionAsync(
            subscription,
            new TestOrderShippedEvent(Guid.NewGuid(), DateTimeOffset.UtcNow, Guid.NewGuid()),
            CancellationToken.None);

        observer.AttemptCalls.Should().Be(1);
        observer.CompletedCalls.Should().Be(1);
    }

    [Fact]
    public async Task DispatchToSubscriptionAsync_ObserverThrows_DoesNotAffectDeliveryOutcome()
    {
        var subscription = Subscription();
        var store = new FakeWebhookSubscriptionStore([subscription]);

        using var handler = new StubHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK));
        using var harness = new WebhookTestHarness(
            handler,
            store,
            o => o.MaxAttempts = 1,
            services => services.AddSingleton<IWebhookDeliveryObserver, ThrowingObserver>());

        var act = async () => await harness.Dispatcher.DispatchToSubscriptionAsync(
            subscription,
            new TestOrderShippedEvent(Guid.NewGuid(), DateTimeOffset.UtcNow, Guid.NewGuid()),
            CancellationToken.None);

        var result = await act.Should().NotThrowAsync();
        result.Subject.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task WithDeliveryObserver_RegistersObserverThatParticipatesInDelivery()
    {
        var subscription = Subscription();
        var store = new FakeWebhookSubscriptionStore([subscription]);
        StaticCountingObserver.AttemptCalls = 0;
        StaticCountingObserver.CompletedCalls = 0;

        using var handler = new StubHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK));
        using var harness = new WebhookTestHarness(
            handler,
            store,
            o => o.MaxAttempts = 1,
            services => services.WithDeliveryObserver<StaticCountingObserver>());

        await harness.Dispatcher.DispatchToSubscriptionAsync(
            subscription,
            new TestOrderShippedEvent(Guid.NewGuid(), DateTimeOffset.UtcNow, Guid.NewGuid()),
            CancellationToken.None);

        StaticCountingObserver.AttemptCalls.Should().Be(1);
        StaticCountingObserver.CompletedCalls.Should().Be(1);
    }
}

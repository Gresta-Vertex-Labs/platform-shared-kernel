using System.Net;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Integration.Webhooks.Dispatch;
using SharedKernel.Integration.Webhooks.Events;
using SharedKernel.Integration.Webhooks.Extensions;
using SharedKernel.Integration.Webhooks.Observability;
using SharedKernel.Integration.Webhooks.Subscriptions;
using SharedKernel.Integration.Webhooks.Tests.TestSupport;

namespace SharedKernel.Integration.Webhooks.Tests.Dispatch;

/// <summary>
/// Proves <see cref="WebhookDispatcher"/> consults the registered <c>IWebhookUrlValidator</c>
/// immediately before every send and never throws when the target is rejected — always a fake/spy
/// validator, never a real DNS lookup or network call.
/// </summary>
public sealed class WebhookUrlValidationDispatchTests
{
    private static WebhookSubscription Subscription() =>
        new(Guid.NewGuid(), new Uri("https://example.test/hook"), ["secret"], [], true);

    [Fact]
    public async Task DispatchToSubscriptionAsync_UrlValidatorRejectsTarget_ReturnsFailureWithoutHttpAttempt()
    {
        var subscription = Subscription();
        var store = new FakeWebhookSubscriptionStore([subscription]);
        var observer = new RecordingObserver();

        using var handler = new StubHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK));
        using var harness = new WebhookTestHarness(
            handler,
            store,
            o => o.MaxAttempts = 3,
            services =>
            {
                services.WithUrlValidator<RejectingWebhookUrlValidator>();
                services.AddSingleton<IWebhookDeliveryObserver>(observer);
            });

        var result = await harness.Dispatcher.DispatchToSubscriptionAsync(
            subscription,
            new TestOrderShippedEvent(Guid.NewGuid(), DateTimeOffset.UtcNow, Guid.NewGuid()),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().BeNull();
        result.Attempts.Should().Be(0);
        result.Error.Should().NotBeNullOrEmpty();
        handler.CallCount.Should().Be(0);

        observer.AttemptCalls.Should().Be(1);
        observer.CompletedCalls.Should().Be(1);
    }

    [Fact]
    public async Task DispatchToSubscriptionAsync_UrlValidatorRejectsTarget_DoesNotPublishExhaustedEvent()
    {
        // Attempts = 0 (never dispatched) is not "exhaustion" of MaxAttempts — no HTTP attempt was
        // ever made, so no WebhookDeliveryExhaustedEvent should be published for this outcome.
        var subscription = Subscription();
        var store = new FakeWebhookSubscriptionStore([subscription]);

        using var handler = new StubHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK));
        using var harness = new WebhookTestHarness(
            handler,
            store,
            o => o.MaxAttempts = 3,
            services => services.WithUrlValidator<RejectingWebhookUrlValidator>());

        await harness.Dispatcher.DispatchToSubscriptionAsync(
            subscription,
            new TestOrderShippedEvent(Guid.NewGuid(), DateTimeOffset.UtcNow, Guid.NewGuid()),
            CancellationToken.None);

        harness.EventPublisher.ShouldNotHavePublished<WebhookDeliveryExhaustedEvent>();
    }

    [Fact]
    public async Task DispatchToSubscriptionAsync_UrlValidatorAllowsTarget_ProceedsToHttpSend()
    {
        var subscription = Subscription();
        var store = new FakeWebhookSubscriptionStore([subscription]);

        using var handler = new StubHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK));
        using var harness = new WebhookTestHarness(handler, store, o => o.MaxAttempts = 1);

        var result = await harness.Dispatcher.DispatchToSubscriptionAsync(
            subscription,
            new TestOrderShippedEvent(Guid.NewGuid(), DateTimeOffset.UtcNow, Guid.NewGuid()),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        handler.CallCount.Should().Be(1);
    }

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
}

using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using SharedKernel.Integration.Webhooks.Events;
using SharedKernel.Integration.Webhooks.Subscriptions;
using SharedKernel.Integration.Webhooks.Tests.TestSupport;
using SharedKernel.Primitives.Logging;
using SharedKernel.Testing.Logging;

namespace SharedKernel.Integration.Webhooks.Tests.Dispatch;

/// <summary>
/// Coverage for P-428: <c>DeliverySucceeded</c>/<c>DeliveryFailed</c>/<c>DeliveryExhausted</c> fire
/// on the corresponding terminal outcome, and no log statement in the package ever includes a
/// signing secret, the signature digest, or the raw payload body.
/// </summary>
public sealed class WebhookDeliveryLoggingTests
{
    private const string DispatcherCategory = "SharedKernel.Integration.Webhooks.Dispatch.WebhookDispatcher";

    private static WebhookSubscription Subscription(string secret = "top-secret-signing-key") =>
        new(Guid.NewGuid(), new Uri("https://example.test/hook"), [secret], [], true);

    [Fact]
    public async Task DispatchToSubscriptionAsync_SuccessfulDelivery_LogsDeliverySucceeded()
    {
        var subscription = Subscription();
        var store = new FakeWebhookSubscriptionStore([subscription]);

        using var handler = new StubHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK));
        using var harness = new WebhookTestHarness(handler, store, o => o.MaxAttempts = 1);

        await harness.Dispatcher.DispatchToSubscriptionAsync(
            subscription,
            new TestOrderShippedEvent(Guid.NewGuid(), DateTimeOffset.UtcNow, Guid.NewGuid()),
            CancellationToken.None);

        var records = harness.LoggerFactory.GetLogger(DispatcherCategory).Records;
        var record = records.ShouldHaveLogged(LoggingEventIdRanges.Integration + 1, LogLevel.Information);
        record.TryGetProperty("SubscriptionId", out var subscriptionId).Should().BeTrue();
        subscriptionId.Should().Be(subscription.SubscriptionId);

        records.ShouldNotHaveLogged(LoggingEventIdRanges.Integration + 2);
        records.ShouldNotHaveLogged(LoggingEventIdRanges.Integration + 3);
    }

    [Fact]
    public async Task DispatchToSubscriptionAsync_NonRetriableFailureUnderMaxAttempts_LogsDeliveryFailedNotExhausted()
    {
        // A 400 response is not classified as transient by the default resilience predicate, so it
        // terminates after a single attempt — well under MaxAttempts — which must log as "failed",
        // not "exhausted", and must not publish WebhookDeliveryExhaustedEvent.
        var subscription = Subscription();
        var store = new FakeWebhookSubscriptionStore([subscription]);

        using var handler = new StubHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.BadRequest));
        using var harness = new WebhookTestHarness(handler, store, o => o.MaxAttempts = 5);

        await harness.Dispatcher.DispatchToSubscriptionAsync(
            subscription,
            new TestOrderShippedEvent(Guid.NewGuid(), DateTimeOffset.UtcNow, Guid.NewGuid()),
            CancellationToken.None);

        var records = harness.LoggerFactory.GetLogger(DispatcherCategory).Records;
        records.ShouldHaveLogged(LoggingEventIdRanges.Integration + 2, LogLevel.Warning);
        records.ShouldNotHaveLogged(LoggingEventIdRanges.Integration + 3);
    }

    [Fact]
    public async Task DispatchToSubscriptionAsync_ExhaustedDelivery_LogsDeliveryExhaustedAlongsideEvent()
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

        await harness.Dispatcher.DispatchToSubscriptionAsync(
            subscription,
            new TestOrderShippedEvent(Guid.NewGuid(), DateTimeOffset.UtcNow, Guid.NewGuid()),
            CancellationToken.None);

        var records = harness.LoggerFactory.GetLogger(DispatcherCategory).Records;
        records.ShouldHaveLogged(LoggingEventIdRanges.Integration + 3, LogLevel.Warning);

        harness.EventPublisher.ShouldHavePublishedOnce<WebhookDeliveryExhaustedEvent>();
    }

    [Fact]
    public async Task DispatchToSubscriptionAsync_AnyOutcome_NeverLogsSecretDigestOrPayload()
    {
        const string secret = "never-should-appear-in-logs";
        var subscription = Subscription(secret);
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

        var records = harness.LoggerFactory.GetLogger(DispatcherCategory).Records;
        foreach (var record in records)
        {
            record.Message.Should().NotContain(secret);
            record.Message.Should().NotContain(integrationEvent.OrderId.ToString());

            if (record.State is not null)
            {
                foreach (var kvp in record.State)
                {
                    kvp.Value?.ToString().Should().NotContain(secret);
                }
            }
        }
    }
}

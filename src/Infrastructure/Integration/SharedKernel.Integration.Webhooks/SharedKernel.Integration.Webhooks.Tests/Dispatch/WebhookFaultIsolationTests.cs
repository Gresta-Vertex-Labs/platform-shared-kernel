using System.Net;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using SharedKernel.Cryptography.Symmetric;
using SharedKernel.Integration.Webhooks.Dispatch;
using SharedKernel.Integration.Webhooks.Events;
using SharedKernel.Integration.Webhooks.Observability;
using SharedKernel.Integration.Webhooks.Subscriptions;
using SharedKernel.Integration.Webhooks.Tests.TestSupport;
using SharedKernel.Primitives.Logging;
using SharedKernel.Testing.Logging;
using SharedKernel.Testing.Telemetry;

namespace SharedKernel.Integration.Webhooks.Tests.Dispatch;

/// <summary>
/// Rule 8: one subscription's fault never aborts the fan-out. Every route into an unexpected exception
/// (validator, encryption service, transport) becomes a failed <see cref="WebhookDeliveryResult"/> with a real delivery id,
/// siblings are delivered, observers and the span see the outcome, and only caller cancellation propagates.
/// </summary>
public sealed class WebhookFaultIsolationTests
{
    private const string DispatcherCategory = "SharedKernel.Integration.Webhooks.Dispatch.WebhookDispatcher";
    private const string SpanName = "webhook deliver";

    private static WebhookSubscription Subscription(string host, string[]? secrets = null) =>
        new(Guid.NewGuid(), new Uri($"https://{host}.example.test/hook"), secrets ?? ["secret"], [], true);

    private static TestOrderShippedEvent NewEvent() => new(Guid.NewGuid(), DateTimeOffset.UtcNow, Guid.NewGuid());

    private static EventId Id(int offset) => new(LoggingEventIdRanges.Integration + offset);

    // ---- fan-out isolation through every route ------------------------------------------------------------------

    public enum FaultRoute
    {
        ThrowingValidator,
        ThrowingEncryptionService,
        UnlistedTransportException,
    }

    [Theory]
    [InlineData(FaultRoute.ThrowingValidator, 1)]
    [InlineData(FaultRoute.ThrowingValidator, 8)]
    [InlineData(FaultRoute.ThrowingEncryptionService, 1)]
    [InlineData(FaultRoute.ThrowingEncryptionService, 8)]
    [InlineData(FaultRoute.UnlistedTransportException, 1)]
    [InlineData(FaultRoute.UnlistedTransportException, 8)]
    public async Task DispatchAsync_OneSubscriptionFaults_ReturnsAllResultsAndDeliversSiblings(FaultRoute route, int maxConcurrentDeliveries)
    {
        var faulty = Subscription("faulty");
        var siblings = new[] { Subscription("one"), Subscription("two") };
        var store = new FakeWebhookSubscriptionStore([faulty, .. siblings]);
        var fault = new InvalidOperationException("fault-sentinel");
        var observer = new RecordingObserver();

        using var spans = ActivityRecorder.StartRecording(WebhookIntegrationActivitySource.Name);
        using var handler = new StubHttpMessageHandler((request, _) =>
            route == FaultRoute.UnlistedTransportException && request.RequestUri!.Host.StartsWith("faulty", StringComparison.Ordinal)
                ? throw fault
                : new HttpResponseMessage(HttpStatusCode.OK));
        using var harness = new WebhookTestHarness(
            handler,
            store,
            o =>
            {
                o.MaxAttempts = 1;
                o.MaxConcurrentDeliveries = maxConcurrentDeliveries;
                o.EncryptPayload = route == FaultRoute.ThrowingEncryptionService;
            },
            services =>
            {
                services.AddSingleton<IWebhookDeliveryObserver>(observer);
                if (route == FaultRoute.ThrowingValidator)
                {
                    services.AddSingleton<IWebhookUrlValidator>(new SelectiveValidator("faulty", fault));
                }

                if (route == FaultRoute.ThrowingEncryptionService)
                {
                    services.AddSingleton(ThrowingEncryptionServiceFor(faulty, fault));
                }
            });

        var results = await harness.Dispatcher.DispatchAsync(NewEvent(), CancellationToken.None);

        results.Should().HaveCount(3);
        var faultedResult = results.Single(r => r.SubscriptionId == faulty.SubscriptionId);
        faultedResult.IsSuccess.Should().BeFalse();
        faultedResult.DeliveryId.Should().NotBeEmpty();
        faultedResult.StatusCode.Should().BeNull();
        faultedResult.Error.Should().Be(WebhookErrors.DeliveryFaulted());
        results.Where(r => r.SubscriptionId != faulty.SubscriptionId).Should().OnlyContain(r => r.IsSuccess);

        // Siblings were really delivered over HTTP (the gate was released even when bounded to one).
        var deliveredHosts = handler.Requests.Select(r => r.RequestUri!.Host).ToList();
        deliveredHosts.Should().Contain(["one.example.test", "two.example.test"]);

        // Observers saw the faulted completion, with the same result the caller received.
        observer.Completed.Should().HaveCount(3);
        observer.Completed.Should().ContainSingle(c => c.Subscription.SubscriptionId == faulty.SubscriptionId && c.Result == faultedResult);

        // No exhaustion event for a fault.
        harness.EventPublisher.ShouldNotHavePublished<WebhookDeliveryExhaustedEvent>();

        // Logged once at Error, carrying the exact exception instance.
        var records = harness.LoggerFactory.GetLogger(DispatcherCategory).Records;
        records.ShouldHaveLoggedCount(Id(5), 1);
        var record = records.ShouldHaveLogged(Id(5), LogLevel.Error);
        record.Exception.Should().BeSameAs(fault);
        records.ShouldNotHaveLogged(Id(2));
        records.ShouldNotHaveLogged(Id(3));

        // The delivery span records the exception.
        var span = FindSpan(spans, faulty.SubscriptionId);
        span.ShouldBeRecordedException(typeof(InvalidOperationException));
    }

    [Fact]
    public async Task DispatchAsync_SubscriptionWithoutSecrets_IsAFailedResultNotAnException()
    {
        var broken = Subscription("broken", secrets: []);
        var healthy = Subscription("healthy");
        var store = new FakeWebhookSubscriptionStore([broken, healthy]);
        var observer = new RecordingObserver();

        using var handler = new StubHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK));
        using var harness = new WebhookTestHarness(
            handler,
            store,
            o => o.MaxAttempts = 1,
            services => services.AddSingleton<IWebhookDeliveryObserver>(observer));

        var results = await harness.Dispatcher.DispatchAsync(NewEvent(), CancellationToken.None);

        results.Should().HaveCount(2);
        var brokenResult = results.Single(r => r.SubscriptionId == broken.SubscriptionId);
        brokenResult.IsSuccess.Should().BeFalse();
        brokenResult.DeliveryId.Should().NotBeEmpty();
        brokenResult.Attempts.Should().Be(0);
        brokenResult.Error.Should().Be(WebhookErrors.SecretsMissing());
        results.Single(r => r.SubscriptionId == healthy.SubscriptionId).IsSuccess.Should().BeTrue();

        handler.Requests.Should().ContainSingle();
        observer.Completed.Should().HaveCount(2);
        harness.EventPublisher.ShouldNotHavePublished<WebhookDeliveryExhaustedEvent>();
        harness.LoggerFactory.GetLogger(DispatcherCategory).Records.ShouldNotHaveLogged(Id(5));
    }

    [Fact]
    public async Task SendTestDeliveryAsync_FaultingValidator_ReturnsFailedResult()
    {
        var subscription = Subscription("faulty");
        var store = new FakeWebhookSubscriptionStore([subscription]);

        using var handler = new StubHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK));
        using var harness = new WebhookTestHarness(
            handler,
            store,
            o => o.MaxAttempts = 1,
            services => services.AddSingleton<IWebhookUrlValidator>(new SelectiveValidator("faulty", new InvalidOperationException("boom"))));

        var result = await harness.Dispatcher.SendTestDeliveryAsync(subscription, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.DeliveryId.Should().NotBeEmpty();
        result.Error.Should().Be(WebhookErrors.DeliveryFaulted());
        handler.CallCount.Should().Be(0);
    }

    // ---- caller cancellation ------------------------------------------------------------------------------------

    [Fact]
    public async Task DispatchAsync_CallerCancelsDuringFanOut_PropagatesAndDispatcherStaysUsable()
    {
        var subscriptions = new[] { Subscription("a"), Subscription("b") };
        var store = new FakeWebhookSubscriptionStore(subscriptions);
        var blockSends = true;

        using var handler = new GatedHandler(() => blockSends);
        using var harness = new WebhookTestHarness(
            handler,
            store,
            o =>
            {
                o.MaxAttempts = 1;
                o.MaxConcurrentDeliveries = 1;
            });

        using var cts = new CancellationTokenSource();
        var dispatch = harness.Dispatcher.DispatchAsync(NewEvent(), cts.Token);
        await handler.FirstSendStarted;
        await cts.CancelAsync();

        var act = async () => await dispatch;
        await act.Should().ThrowAsync<OperationCanceledException>();

        // Not converted into a failed result, and a fresh fan-out on the same dispatcher still delivers.
        blockSends = false;
        var results = await harness.Dispatcher.DispatchAsync(NewEvent(), CancellationToken.None);
        results.Should().HaveCount(2).And.OnlyContain(r => r.IsSuccess);
    }

    [Fact]
    public async Task SendTestDeliveryAsync_CallerCancels_Propagates()
    {
        var subscription = Subscription("a");
        var store = new FakeWebhookSubscriptionStore([subscription]);

        using var handler = new GatedHandler(() => true);
        using var harness = new WebhookTestHarness(handler, store, o => o.MaxAttempts = 1);

        using var cts = new CancellationTokenSource();
        var delivery = harness.Dispatcher.SendTestDeliveryAsync(subscription, cts.Token);
        await handler.FirstSendStarted;
        await cts.CancelAsync();

        var act = async () => await delivery;
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ---- exhaustion publish isolation ---------------------------------------------------------------------------

    public enum PublisherBehaviour
    {
        ReturnsFailedResult,
        Throws,
    }

    [Theory]
    [InlineData(PublisherBehaviour.ReturnsFailedResult)]
    [InlineData(PublisherBehaviour.Throws)]
    public async Task DispatchToSubscriptionAsync_ExhaustionPublisherFails_OutcomeUnchangedAndLoggedOnceWithoutMessage(PublisherBehaviour behaviour)
    {
        const string sentinel = "publisher-sentinel-message";
        var subscription = Subscription("down");
        var store = new FakeWebhookSubscriptionStore([subscription]);
        var thrown = new InvalidOperationException(sentinel);

        var publisher = Substitute.For<SharedKernel.Messaging.Abstractions.EventPublisher.IEventPublisher>();
        publisher
            .PublishAsync(Arg.Any<WebhookDeliveryExhaustedEvent>(), Arg.Any<CancellationToken>())
            .Returns(_ => behaviour == PublisherBehaviour.Throws
                ? throw thrown
                : Task.FromResult(SharedKernel.Primitives.Results.Result.Failure(
                    SharedKernel.Primitives.Errors.Error.Unavailable("messaging.unavailable", sentinel))));

        using var handler = new StubHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        using var harness = new WebhookTestHarness(
            handler,
            store,
            o =>
            {
                o.MaxAttempts = 2;
                o.BaseBackoffDelay = TimeSpan.FromMilliseconds(1);
                o.MaxBackoffDelay = TimeSpan.FromMilliseconds(5);
            },
            services => services.AddSingleton(publisher));

        var result = await harness.Dispatcher.DispatchToSubscriptionAsync(subscription, NewEvent(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Attempts.Should().Be(2);
        result.Error.Should().Be(WebhookErrors.EndpointResponded(503));

        var records = harness.LoggerFactory.GetLogger(DispatcherCategory).Records;
        records.ShouldHaveLogged(Id(3), LogLevel.Warning);
        records.Select(r => r with { Exception = null }).ToList().ShouldNotLeak(sentinel);
        if (behaviour == PublisherBehaviour.Throws)
        {
            records.ShouldHaveLoggedCount(Id(6), 1);
            records.ShouldHaveLoggedWithException(Id(6), typeof(InvalidOperationException)).Exception.Should().BeSameAs(thrown);
            records.ShouldNotHaveLogged(Id(4));
        }
        else
        {
            records.ShouldHaveLoggedCount(Id(4), 1);
            records.ShouldHaveLogged(Id(4), LogLevel.Warning);
        }
    }

    [Fact]
    public async Task DispatchToSubscriptionAsync_ExhaustedDelivery_StillPublishesExactlyOneEventCarryingTheFixedReason()
    {
        var subscription = Subscription("down");
        var store = new FakeWebhookSubscriptionStore([subscription]);

        using var handler = new StubHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        using var harness = new WebhookTestHarness(handler, store, o =>
        {
            o.MaxAttempts = 2;
            o.BaseBackoffDelay = TimeSpan.FromMilliseconds(1);
            o.MaxBackoffDelay = TimeSpan.FromMilliseconds(5);
        });

        await harness.Dispatcher.DispatchToSubscriptionAsync(subscription, NewEvent(), CancellationToken.None);

        var published = harness.EventPublisher.ShouldHavePublishedOnce<WebhookDeliveryExhaustedEvent>();
        published.LastErrorCode.Should().Be(WebhookErrorCodes.EndpointUnavailable);
    }

    // ---- helpers ------------------------------------------------------------------------------------------------

    private static System.Diagnostics.Activity FindSpan(ActivityRecorder spans, Guid subscriptionId) =>
        spans.RecordedActivities
            .Where(a => a.OperationName == SpanName)
            .Single(a => a.GetTagItem(WebhookTagKeys.SubscriptionId) is Guid id && id == subscriptionId);

    private static ISymmetricEncryptionService ThrowingEncryptionServiceFor(WebhookSubscription faulty, Exception fault)
    {
        var service = Substitute.For<ISymmetricEncryptionService>();
        service
            .EncryptToStringAsync(Arg.Any<string>(), Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                // The associated data begins with the subscription id (WebhookPayloadAssociatedData.Build).
                var aad = System.Text.Encoding.UTF8.GetString(call.ArgAt<ReadOnlyMemory<byte>>(1).Span);
                return aad.Contains(faulty.SubscriptionId.ToString(), StringComparison.OrdinalIgnoreCase)
                    ? throw fault
                    : new ValueTask<string>("ciphertext");
            });
        return service;
    }

    private sealed class SelectiveValidator(string faultyHostPrefix, Exception fault) : IWebhookUrlValidator
    {
        public Task<bool> ValidateAsync(Uri url, CancellationToken ct) =>
            url.Host.StartsWith(faultyHostPrefix, StringComparison.Ordinal) ? throw fault : Task.FromResult(true);
    }

    private sealed class RecordingObserver : IWebhookDeliveryObserver
    {
        private readonly List<(WebhookSubscription Subscription, WebhookDeliveryResult Result)> _completed = [];

        public IReadOnlyList<(WebhookSubscription Subscription, WebhookDeliveryResult Result)> Completed
        {
            get
            {
                lock (_completed)
                {
                    return [.. _completed];
                }
            }
        }

        public Task OnAttemptAsync(WebhookSubscription subscription, int attemptNumber, CancellationToken ct) => Task.CompletedTask;

        public Task OnCompletedAsync(WebhookSubscription subscription, WebhookDeliveryResult result, CancellationToken ct)
        {
            lock (_completed)
            {
                _completed.Add((subscription, result));
            }

            return Task.CompletedTask;
        }
    }

    /// <summary>Blocks every send until the caller's token is cancelled while <c>shouldBlock</c> returns true.</summary>
    private sealed class GatedHandler(Func<bool> shouldBlock) : HttpMessageHandler
    {
        private readonly TaskCompletionSource _firstSendStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task FirstSendStarted => _firstSendStarted.Task;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (shouldBlock())
            {
                _firstSendStarted.TrySetResult();
                await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            }

            return new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = request };
        }
    }
}

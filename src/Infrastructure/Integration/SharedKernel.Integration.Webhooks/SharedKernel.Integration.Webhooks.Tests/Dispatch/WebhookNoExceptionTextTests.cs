using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Polly.CircuitBreaker;
using Polly.Timeout;
using SharedKernel.Cryptography.Symmetric;
using SharedKernel.Integration.Webhooks.Dispatch;
using SharedKernel.Integration.Webhooks.Events;
using SharedKernel.Integration.Webhooks.Subscriptions;
using SharedKernel.Integration.Webhooks.Tests.TestSupport;
using SharedKernel.Primitives.Logging;
using SharedKernel.Testing.Logging;
using SharedKernel.Testing.Telemetry;

namespace SharedKernel.Integration.Webhooks.Tests.Dispatch;

/// <summary>
/// Rule 24: exception text never reaches a <see cref="WebhookDeliveryResult"/>, a log record, the exhaustion event or
/// a span tag/description. A failure carries a fixed, closed reason; the exception object itself is the logged
/// exception (last parameter) and the span's exception event.
/// </summary>
public sealed class WebhookNoExceptionTextTests
{
    private const string Sentinel = "https://sentinel-host.invalid/bearer-sentinel-token";
    private const string DispatcherCategory = "SharedKernel.Integration.Webhooks.Dispatch.WebhookDispatcher";

    private static EventId Id(int offset) => new(LoggingEventIdRanges.Integration + offset);

    public static TheoryData<string, Func<Exception>, string> TransportFaults => new()
    {
        { "HttpRequestException", () => new HttpRequestException(Sentinel), WebhookErrorCodes.Unavailable },
        { "TimeoutRejectedException", () => new TimeoutRejectedException(Sentinel), WebhookErrorCodes.Timeout },
        { "BrokenCircuitException", () => new BrokenCircuitException(Sentinel), WebhookErrorCodes.Unavailable },
        { "TimeoutException", () => new TimeoutException(Sentinel), WebhookErrorCodes.Timeout },
    };

    [Theory]
    [MemberData(nameof(TransportFaults))]
    public async Task DispatchToSubscriptionAsync_TransportFault_ResultLogEventAndSpanCarryNoExceptionText(
        string name,
        Func<Exception> createFault,
        string expectedReason)
    {
        _ = name;
        var fault = createFault();
        var subscription = new WebhookSubscription(Guid.NewGuid(), new Uri("https://example.test/hook"), ["super-secret"], [], true);
        var store = new FakeWebhookSubscriptionStore([subscription]);

        using var spans = ActivityRecorder.StartRecording(WebhookIntegrationActivitySource.Name);
        using var handler = new StubHttpMessageHandler((_, _) => throw fault);
        using var harness = new WebhookTestHarness(handler, store, o =>
        {
            o.MaxAttempts = 2;
            o.BaseBackoffDelay = TimeSpan.FromMilliseconds(1);
            o.MaxBackoffDelay = TimeSpan.FromMilliseconds(5);
        });

        var result = await harness.Dispatcher.DispatchToSubscriptionAsync(
            subscription,
            new TestOrderShippedEvent(Guid.NewGuid(), DateTimeOffset.UtcNow, Guid.NewGuid()),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(expectedReason);
        result.Error.Message.Should().NotContain("sentinel");

        var records = harness.LoggerFactory.GetLogger(DispatcherCategory).Records;
        records.Select(r => r with { Exception = null }).ToList().ShouldNotLeak(Sentinel, "sentinel-host", "bearer-sentinel-token", "super-secret");

        // Only the exception OBJECT travels to the log, as the last parameter, and it is the thrown instance.
        var failureRecord = records.Single(r => r.EventId == Id(2) || r.EventId == Id(3));
        failureRecord.Exception.Should().BeSameAs(fault);

        // The exhaustion event carries the fixed reason, never the exception text.
        if (harness.EventPublisher.PublishedOf<WebhookDeliveryExhaustedEvent>() is { Count: > 0 } published)
        {
            published.Should().OnlyContain(e => e.LastErrorCode == expectedReason);
            published.Should().NotContain(e => e.LastErrorCode!.Contains("sentinel"));
        }

        var span = spans.RecordedActivities
            .Where(a => a.OperationName == "webhook deliver")
            .Single(a => a.GetTagItem(WebhookTagKeys.SubscriptionId) is Guid id && id == subscription.SubscriptionId);
        span.ShouldNotLeak(Sentinel, "sentinel-host", "bearer-sentinel-token", "super-secret");
    }

    [Fact]
    public async Task DispatchToSubscriptionAsync_ThrowingValidatorAndEncryptionService_CarryNoExceptionTextAndLogTheException()
    {
        var validatorFault = new InvalidOperationException(Sentinel);
        var encryptionFault = new InvalidOperationException(Sentinel);

        var rejectedBySubscription = new WebhookSubscription(Guid.NewGuid(), new Uri("https://validator.example.test/hook"), ["s1"], [], true);
        var encryptedSubscription = new WebhookSubscription(Guid.NewGuid(), new Uri("https://encrypt.example.test/hook"), ["s2"], [], true);

        var validatorStore = new FakeWebhookSubscriptionStore([rejectedBySubscription]);
        using var validatorHandler = new StubHttpMessageHandler((_, _) => new HttpResponseMessage(System.Net.HttpStatusCode.OK));
        using var validatorHarness = new WebhookTestHarness(
            validatorHandler,
            validatorStore,
            o => o.MaxAttempts = 1,
            services => services.AddSingleton<IWebhookUrlValidator>(new ThrowingValidator(validatorFault)));

        var validatorResult = await validatorHarness.Dispatcher.DispatchToSubscriptionAsync(
            rejectedBySubscription,
            new TestOrderShippedEvent(Guid.NewGuid(), DateTimeOffset.UtcNow, Guid.NewGuid()),
            CancellationToken.None);

        var encryption = Substitute.For<ISymmetricEncryptionService>();
        encryption
            .EncryptToStringAsync(Arg.Any<string>(), Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<CancellationToken>())
            .Returns<ValueTask<string>>(_ => throw encryptionFault);
        var encryptionStore = new FakeWebhookSubscriptionStore([encryptedSubscription]);
        using var encryptionHandler = new StubHttpMessageHandler((_, _) => new HttpResponseMessage(System.Net.HttpStatusCode.OK));
        using var encryptionHarness = new WebhookTestHarness(
            encryptionHandler,
            encryptionStore,
            o =>
            {
                o.MaxAttempts = 1;
                o.EncryptPayload = true;
            },
            services => services.AddSingleton(encryption));

        var encryptionResult = await encryptionHarness.Dispatcher.DispatchToSubscriptionAsync(
            encryptedSubscription,
            new TestOrderShippedEvent(Guid.NewGuid(), DateTimeOffset.UtcNow, Guid.NewGuid()),
            CancellationToken.None);

        foreach (var (result, harness, fault) in new[]
        {
            (validatorResult, validatorHarness, validatorFault),
            (encryptionResult, encryptionHarness, encryptionFault),
        })
        {
            result.Error.Should().Be(WebhookErrors.DeliveryFaulted());
            var records = harness.LoggerFactory.GetLogger(DispatcherCategory).Records;
            records.Select(r => r with { Exception = null }).ToList().ShouldNotLeak(Sentinel);
            records.ShouldHaveLoggedWithException(Id(5), typeof(InvalidOperationException)).Exception.Should().BeSameAs(fault);
        }
    }

    [Fact]
    public async Task DispatchToSubscriptionAsync_NonSuccessStatus_ResultNamesOnlyTheStatusCode()
    {
        var subscription = new WebhookSubscription(Guid.NewGuid(), new Uri("https://example.test/hook"), ["s"], [], true);
        var store = new FakeWebhookSubscriptionStore([subscription]);

        using var handler = new StubHttpMessageHandler((_, _) => new HttpResponseMessage(System.Net.HttpStatusCode.BadRequest)
        {
            ReasonPhrase = "reason-phrase-sentinel",
            Content = new StringContent("body-sentinel"),
        });
        using var harness = new WebhookTestHarness(handler, store, o => o.MaxAttempts = 1);

        var result = await harness.Dispatcher.DispatchToSubscriptionAsync(
            subscription,
            new TestOrderShippedEvent(Guid.NewGuid(), DateTimeOffset.UtcNow, Guid.NewGuid()),
            CancellationToken.None);

        result.Error.Should().Be(WebhookErrors.EndpointResponded(400));
        result.Error!.Code.Should().Be(WebhookErrorCodes.EndpointRejected);
        harness.LoggerFactory.GetLogger(DispatcherCategory).Records.ShouldNotLeak("reason-phrase-sentinel", "body-sentinel");
    }

    private sealed class ThrowingValidator(Exception fault) : IWebhookUrlValidator
    {
        public Task<bool> ValidateAsync(Uri url, CancellationToken ct) => throw fault;
    }
}

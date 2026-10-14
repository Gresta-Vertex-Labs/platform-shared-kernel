using System.Diagnostics;
using System.Net;
using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Polly.CircuitBreaker;
using Polly.Timeout;
using SharedKernel.Contracts.Events;
using SharedKernel.Integration.Webhooks.Diagnostics;
using SharedKernel.Integration.Webhooks.Dispatch;
using SharedKernel.Integration.Webhooks.Events;
using SharedKernel.Integration.Webhooks.Subscriptions;
using SharedKernel.Integration.Webhooks.Tests.TestSupport;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Logging;
using SharedKernel.Primitives.Propagation;
using SharedKernel.Testing.Logging;
using SharedKernel.Testing.Telemetry;

namespace SharedKernel.Integration.Webhooks.Tests.Dispatch;

/// <summary>
/// The observability and error-code conformance of the webhook family: every error condition maps to its code and
/// <see cref="ErrorType"/>, spans and instruments follow the platform conventions, and the log events keep their ids,
/// levels and templates.
/// </summary>
public sealed class WebhookConformanceTests
{
    private const string DispatcherCategory = "SharedKernel.Integration.Webhooks.Dispatch.WebhookDispatcher";
    private const string Sentinel = "https://sentinel-host.invalid/sentinel-token";
    private const string Secret = "conformance-signing-secret";

    private static WebhookSubscription Subscription(string[]? secrets = null, IReadOnlyDictionary<string, string>? headers = null) =>
        new(Guid.NewGuid(), new Uri("https://example.test/hook"), secrets ?? [Secret], [], true, headers);

    private static ConformanceEvent NewEvent() => new(Guid.NewGuid(), DateTimeOffset.UtcNow);

    private static EventId Id(int offset) => new(LoggingEventIdRanges.Integration + offset);

    // --- Error codes ------------------------------------------------------------------------------------------------

    public static TheoryData<string, HttpStatusCode, string, ErrorType> StatusCodes => new()
    {
        { "500", HttpStatusCode.InternalServerError, WebhookErrorCodes.EndpointUnavailable, ErrorType.Unavailable },
        { "503", HttpStatusCode.ServiceUnavailable, WebhookErrorCodes.EndpointUnavailable, ErrorType.Unavailable },
        { "429", HttpStatusCode.TooManyRequests, WebhookErrorCodes.EndpointUnavailable, ErrorType.Unavailable },
        { "408", HttpStatusCode.RequestTimeout, WebhookErrorCodes.EndpointUnavailable, ErrorType.Unavailable },
        { "404", HttpStatusCode.NotFound, WebhookErrorCodes.EndpointRejected, ErrorType.Unexpected },
        { "400", HttpStatusCode.BadRequest, WebhookErrorCodes.EndpointRejected, ErrorType.Unexpected },
    };

    [Theory]
    [MemberData(nameof(StatusCodes))]
    public async Task A_non_success_status_maps_to_its_code_and_error_type(string name, HttpStatusCode status, string code, ErrorType type)
    {
        _ = name;
        using var handler = new StubHttpMessageHandler((_, _) => new HttpResponseMessage(status));
        var subscription = Subscription();
        using var harness = new WebhookTestHarness(handler, new FakeWebhookSubscriptionStore([subscription]), o => o.MaxAttempts = 1);

        var result = await harness.Dispatcher.DispatchToSubscriptionAsync(subscription, NewEvent(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().NotBeNull();
        result.Error!.Code.Should().Be(code);
        result.Error.Type.Should().Be(type);
        result.Error.Message.Should().Be(WebhookErrors.EndpointResponded((int)status).Message).And.Contain(((int)status).ToString());
        result.StatusCode.Should().Be((int)status);
    }

    public static TheoryData<string, Func<Exception>, string, ErrorType> TransportFaults => new()
    {
        { "HttpRequestException", () => new HttpRequestException(Sentinel), WebhookErrorCodes.Unavailable, ErrorType.Unavailable },
        { "BrokenCircuitException", () => new BrokenCircuitException(Sentinel), WebhookErrorCodes.Unavailable, ErrorType.Unavailable },
        { "TimeoutRejectedException", () => new TimeoutRejectedException(Sentinel), WebhookErrorCodes.Timeout, ErrorType.Timeout },
        { "TimeoutException", () => new TimeoutException(Sentinel), WebhookErrorCodes.Timeout, ErrorType.Timeout },
        { "client timeout (TaskCanceledException)", () => new TaskCanceledException(Sentinel), WebhookErrorCodes.Timeout, ErrorType.Timeout },
    };

    [Theory]
    [MemberData(nameof(TransportFaults))]
    public async Task A_transport_failure_maps_to_its_code_and_error_type_with_a_fixed_message(string name, Func<Exception> fault, string code, ErrorType type)
    {
        _ = name;
        var thrown = fault();
        using var handler = new StubHttpMessageHandler((_, _) => throw thrown);
        var subscription = Subscription();
        using var harness = new WebhookTestHarness(handler, new FakeWebhookSubscriptionStore([subscription]), o => o.MaxAttempts = 1);

        var result = await harness.Dispatcher.DispatchToSubscriptionAsync(subscription, NewEvent(), CancellationToken.None);

        result.Error!.Code.Should().Be(code);
        result.Error.Type.Should().Be(type);
        result.Error.Message.Should().NotContain("sentinel");
        result.StatusCode.Should().BeNull();
    }

    [Fact]
    public async Task A_refused_target_a_header_collision_and_missing_secrets_are_validation_errors_and_a_fault_is_unexpected()
    {
        using var handler = new StubHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK));

        var rejected = Subscription();
        using (var harness = new WebhookTestHarness(
            handler,
            new FakeWebhookSubscriptionStore([rejected]),
            o => o.MaxAttempts = 1,
            services => services.AddSingleton<IWebhookUrlValidator>(new RejectingWebhookUrlValidator())))
        {
            var result = await harness.Dispatcher.DispatchToSubscriptionAsync(rejected, NewEvent(), CancellationToken.None);
            result.Error.Should().Be(WebhookErrors.TargetRejected());
            result.Error!.Code.Should().Be(WebhookErrorCodes.TargetRejected);
            result.Error.Type.Should().Be(ErrorType.Validation);
        }

        var colliding = Subscription(headers: new Dictionary<string, string> { ["X-Webhook-Signature"] = "x" });
        using (var harness = new WebhookTestHarness(handler, new FakeWebhookSubscriptionStore([colliding]), o => o.MaxAttempts = 1))
        {
            var result = await harness.Dispatcher.DispatchToSubscriptionAsync(colliding, NewEvent(), CancellationToken.None);
            result.Error.Should().Be(WebhookErrors.HeaderCollision());
            result.Error!.Code.Should().Be(WebhookErrorCodes.HeaderCollision);
            result.Error.Type.Should().Be(ErrorType.Validation);
        }

        var noSecrets = Subscription(secrets: []);
        using (var harness = new WebhookTestHarness(handler, new FakeWebhookSubscriptionStore([noSecrets]), o => o.MaxAttempts = 1))
        {
            var result = await harness.Dispatcher.DispatchToSubscriptionAsync(noSecrets, NewEvent(), CancellationToken.None);
            result.Error.Should().Be(WebhookErrors.SecretsMissing());
            result.Error!.Code.Should().Be(WebhookErrorCodes.SecretsMissing);
            result.Error.Type.Should().Be(ErrorType.Validation);
        }

        var faulting = Subscription();
        using (var harness = new WebhookTestHarness(
            handler,
            new FakeWebhookSubscriptionStore([faulting]),
            o => o.MaxAttempts = 1,
            services => services.AddSingleton<IWebhookUrlValidator>(new ThrowingValidator())))
        {
            var result = await harness.Dispatcher.DispatchToSubscriptionAsync(faulting, NewEvent(), CancellationToken.None);
            result.Error.Should().Be(WebhookErrors.DeliveryFaulted());
            result.Error!.Code.Should().Be(WebhookErrorCodes.DeliveryFaulted);
            result.Error.Type.Should().Be(ErrorType.Unexpected);
        }
    }

    [Fact]
    public void The_error_codes_are_lowercase_integration_webhook_codes_with_no_notification_prefix()
    {
        string[] codes =
        [
            .. typeof(WebhookErrorCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(f => f.IsLiteral)
                .Select(f => (string)f.GetRawConstantValue()!),
        ];

        codes.Should().HaveCount(8);
        codes.Should().OnlyContain(c => c.StartsWith("integration.webhook.", StringComparison.Ordinal) && c == c.ToLowerInvariant());
        codes.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task A_result_carries_an_error_exactly_when_it_failed()
    {
        using var ok = new StubHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK));
        var subscription = Subscription();
        using var harness = new WebhookTestHarness(ok, new FakeWebhookSubscriptionStore([subscription]), o => o.MaxAttempts = 1);

        var success = await harness.Dispatcher.DispatchToSubscriptionAsync(subscription, NewEvent(), CancellationToken.None);

        success.IsSuccess.Should().BeTrue();
        success.Error.Should().BeNull();

        var guid = Guid.NewGuid();
        var inconsistentSuccess = () => new WebhookDeliveryResult(guid, guid, true, 200, 1, WebhookErrors.Unavailable());
        var inconsistentFailure = () => new WebhookDeliveryResult(guid, guid, false, 500, 1, null);
        inconsistentSuccess.Should().Throw<ArgumentException>();
        inconsistentFailure.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task The_exhaustion_event_carries_only_the_last_error_code_on_the_wire()
    {
        using var handler = new StubHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var subscription = Subscription();
        using var harness = new WebhookTestHarness(handler, new FakeWebhookSubscriptionStore([subscription]), o =>
        {
            o.MaxAttempts = 2;
            o.BaseBackoffDelay = TimeSpan.FromMilliseconds(1);
            o.MaxBackoffDelay = TimeSpan.FromMilliseconds(5);
        });

        var result = await harness.Dispatcher.DispatchToSubscriptionAsync(subscription, NewEvent(), CancellationToken.None);

        var published = harness.EventPublisher.ShouldHavePublishedOnce<WebhookDeliveryExhaustedEvent>();
        published.LastErrorCode.Should().Be(result.Error!.Code).And.Be(WebhookErrorCodes.EndpointUnavailable);

        var json = JsonSerializer.Serialize(published, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        json.Should().Contain("\"lastErrorCode\":\"integration.webhook.endpoint_unavailable\"");
        json.Should().NotContain("\"lastError\"");
        json.Should().NotContain("status code");
        IntegrationEventDescriptor.For<WebhookDeliveryExhaustedEvent>().Version.Should().Be(1);
    }

    // --- Spans ------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_successful_delivery_records_a_client_span_with_the_exact_tag_set()
    {
        using var spans = ActivityRecorder.StartRecording(WebhookIntegrationActivitySource.Name);
        using var handler = new StubHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK));
        var subscription = Subscription();
        using var harness = new WebhookTestHarness(handler, new FakeWebhookSubscriptionStore([subscription]), o => o.MaxAttempts = 1);

        var result = await harness.Dispatcher.DispatchToSubscriptionAsync(subscription, NewEvent(), CancellationToken.None);

        var span = DeliverSpan(spans, subscription);
        span.ShouldFollowSpanConvention();
        span.ShouldHaveKind(ActivityKind.Client);
        span.OperationName.Should().Be("webhook deliver");
        span.ShouldHaveTagKeys(
            WebhookTagKeys.SubscriptionId,
            WebhookTagKeys.DeliveryId,
            WebhookTagKeys.EventType,
            WebhookTagKeys.AttemptCount,
            WellKnownTagKeys.Outcome);
        span.ShouldHaveTag(WebhookTagKeys.DeliveryId, result.DeliveryId);
        span.ShouldHaveOutcome(WellKnownOutcomes.Success);
        span.ShouldBeRecordedSuccess();
        span.ShouldNotLeak(subscription.Url.ToString(), Secret);
    }

    [Fact]
    public async Task A_failed_result_is_recorded_as_a_failure_on_the_span()
    {
        using var spans = ActivityRecorder.StartRecording(WebhookIntegrationActivitySource.Name);
        using var handler = new StubHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var subscription = Subscription();
        using var harness = new WebhookTestHarness(handler, new FakeWebhookSubscriptionStore([subscription]), o => o.MaxAttempts = 1);

        var result = await harness.Dispatcher.DispatchToSubscriptionAsync(subscription, NewEvent(), CancellationToken.None);

        var span = DeliverSpan(spans, subscription);
        span.ShouldBeRecordedFailure(result.Error!);
        span.ShouldHaveOutcome(WellKnownOutcomes.Failure);
        span.ShouldNotLeak(subscription.Url.ToString(), Secret);
    }

    [Fact]
    public async Task A_fault_is_recorded_as_an_exception_on_the_span()
    {
        using var spans = ActivityRecorder.StartRecording(WebhookIntegrationActivitySource.Name);
        using var handler = new StubHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK));
        var subscription = Subscription();
        using var harness = new WebhookTestHarness(
            handler,
            new FakeWebhookSubscriptionStore([subscription]),
            o => o.MaxAttempts = 1,
            services => services.AddSingleton<IWebhookUrlValidator>(new ThrowingValidator()));

        await harness.Dispatcher.DispatchToSubscriptionAsync(subscription, NewEvent(), CancellationToken.None);

        var span = DeliverSpan(spans, subscription);
        span.ShouldBeRecordedException(typeof(InvalidOperationException));
        span.ShouldHaveOutcome(WellKnownOutcomes.Exception);
    }

    [Fact]
    public async Task Caller_cancellation_propagates_and_is_not_an_error_on_the_span()
    {
        using var spans = ActivityRecorder.StartRecording(WebhookIntegrationActivitySource.Name);
        using var cts = new CancellationTokenSource();
        using var handler = new StubHttpMessageHandler((_, _) =>
        {
            cts.Cancel();
            throw new OperationCanceledException(cts.Token);
        });
        var subscription = Subscription();
        using var harness = new WebhookTestHarness(handler, new FakeWebhookSubscriptionStore([subscription]), o => o.MaxAttempts = 1);

        var act = () => harness.Dispatcher.DispatchToSubscriptionAsync(subscription, NewEvent(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        var span = DeliverSpan(spans, subscription);
        span.ShouldBeRecordedCancellation();
        span.ShouldHaveOutcome(WellKnownOutcomes.Canceled);
    }

    [Fact]
    public async Task The_fan_out_span_is_an_internal_dispatch_span_with_the_subscription_count()
    {
        using var spans = ActivityRecorder.StartRecording(WebhookIntegrationActivitySource.Name);
        using var handler = new StubHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK));
        var subscription = Subscription();
        using var harness = new WebhookTestHarness(handler, new FakeWebhookSubscriptionStore([subscription]), o => o.MaxAttempts = 1);

        await harness.Dispatcher.DispatchAsync(NewEvent(), CancellationToken.None);

        var child = DeliverSpan(spans, subscription);
        var parent = spans.RecordedActivities.Single(a => a.SpanId == child.ParentSpanId);
        parent.OperationName.Should().Be("webhook dispatch");
        parent.ShouldFollowSpanConvention().ShouldHaveKind(ActivityKind.Internal);
        parent.ShouldHaveTagKeys(WebhookTagKeys.SubscriptionCount, WebhookTagKeys.EventType);
    }

    // --- Instruments -------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Each_terminal_outcome_is_counted_once_with_a_bounded_tag_set()
    {
        using var metrics = MetricRecorder.StartRecording(WebhookMetrics.MeterName);
        using var handler = new StubHttpMessageHandler((_, call) => new HttpResponseMessage(call == 1 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK));
        var subscription = Subscription();
        using var harness = new WebhookTestHarness(handler, new FakeWebhookSubscriptionStore([subscription]), o =>
        {
            o.MaxAttempts = 3;
            o.BaseBackoffDelay = TimeSpan.FromMilliseconds(1);
            o.MaxBackoffDelay = TimeSpan.FromMilliseconds(5);
        });

        var success = await harness.Dispatcher.DispatchToSubscriptionAsync(subscription, NewEvent(), CancellationToken.None);

        success.IsSuccess.Should().BeTrue();
        success.Attempts.Should().Be(2);

        KeyValuePair<string, object?> eventTag = new(WebhookTagKeys.EventType, ConformanceEvent.EventName);
        metrics.Count(WebhookMetrics.DeliveriesInstrumentName, eventTag).Should().Be(1, "a retried delivery counts once");
        metrics.Sum(WebhookMetrics.DeliveriesInstrumentName, eventTag, new(WellKnownTagKeys.Outcome, WellKnownOutcomes.Success)).Should().Be(1);
        metrics.Count(WebhookMetrics.DeliveryDurationInstrumentName, eventTag).Should().Be(1);

        metrics.ShouldHaveInstrument(WebhookMetrics.DeliveriesInstrumentName, "{delivery}", InstrumentKind.Counter);
        metrics.ShouldHaveInstrument(WebhookMetrics.DeliveryDurationInstrumentName, "s", InstrumentKind.Histogram);
        metrics.ShouldHaveTagSet(WebhookMetrics.DeliveriesInstrumentName, WebhookTagKeys.EventType, WellKnownTagKeys.Outcome);
        metrics.ShouldHaveTagSet(WebhookMetrics.DeliveryDurationInstrumentName, WebhookTagKeys.EventType, WellKnownTagKeys.Outcome);
        metrics.ShouldNotHaveTagKey(WebhookMetrics.DeliveriesInstrumentName, WebhookTagKeys.SubscriptionId);
        metrics.ShouldNotHaveTagKey(WebhookMetrics.DeliveriesInstrumentName, WebhookTagKeys.DeliveryId);
        metrics.ShouldUseOutcomeVocabulary(WebhookMetrics.DeliveriesInstrumentName);
        metrics.ShouldNotLeak(subscription.Url.ToString(), Secret, subscription.SubscriptionId.ToString(), success.DeliveryId.ToString());
        metrics.ShouldFollowMetricConvention();
    }

    [Fact]
    public async Task Failure_fault_and_cancellation_are_counted_with_their_outcome()
    {
        using var metrics = MetricRecorder.StartRecording(WebhookMetrics.MeterName);
        var failing = Subscription();
        var faulting = Subscription();
        var canceling = Subscription();
        using var cts = new CancellationTokenSource();

        using var failHandler = new StubHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.BadRequest));
        using var failHarness = new WebhookTestHarness(failHandler, new FakeWebhookSubscriptionStore([failing]), o => o.MaxAttempts = 1);
        await failHarness.Dispatcher.DispatchToSubscriptionAsync(failing, NewEvent(), CancellationToken.None);

        using var faultHarness = new WebhookTestHarness(
            failHandler,
            new FakeWebhookSubscriptionStore([faulting]),
            o => o.MaxAttempts = 1,
            services => services.AddSingleton<IWebhookUrlValidator>(new ThrowingValidator()));
        await faultHarness.Dispatcher.DispatchToSubscriptionAsync(faulting, NewEvent(), CancellationToken.None);

        using var cancelHandler = new StubHttpMessageHandler((_, _) =>
        {
            cts.Cancel();
            throw new OperationCanceledException(cts.Token);
        });
        using var cancelHarness = new WebhookTestHarness(cancelHandler, new FakeWebhookSubscriptionStore([canceling]), o => o.MaxAttempts = 1);
        var act = () => cancelHarness.Dispatcher.DispatchToSubscriptionAsync(canceling, NewEvent(), cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();

        KeyValuePair<string, object?> eventTag = new(WebhookTagKeys.EventType, ConformanceEvent.EventName);
        metrics.Sum(WebhookMetrics.DeliveriesInstrumentName, eventTag, new(WellKnownTagKeys.Outcome, WellKnownOutcomes.Failure)).Should().Be(1);
        metrics.Sum(WebhookMetrics.DeliveriesInstrumentName, eventTag, new(WellKnownTagKeys.Outcome, WellKnownOutcomes.Exception)).Should().Be(1);
        metrics.Sum(WebhookMetrics.DeliveriesInstrumentName, eventTag, new(WellKnownTagKeys.Outcome, WellKnownOutcomes.Canceled)).Should().Be(1);
    }

    [Fact]
    public async Task The_webhook_and_notification_families_share_one_meter_name()
    {
        using var metrics = MetricRecorder.StartRecording(WebhookMetrics.MeterName);
        using var handler = new StubHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK));
        var subscription = Subscription();
        using var harness = new WebhookTestHarness(handler, new FakeWebhookSubscriptionStore([subscription]), o => o.MaxAttempts = 1);

        await harness.Dispatcher.DispatchToSubscriptionAsync(subscription, NewEvent(), CancellationToken.None);

        WebhookMetrics.MeterName.Should().Be("SharedKernel.Integration").And.Be(WebhookIntegrationActivitySource.Name);
        metrics.Instruments.Should().OnlyContain(i => i.MeterName == "SharedKernel.Integration");
    }

    // --- Logging -----------------------------------------------------------------------------------------------------

    [Fact]
    public void The_event_ids_are_pinned_and_live_in_the_webhook_sub_block()
    {
        WebhooksLoggingEventIds.ObserverException.Should().Be(15000);
        WebhooksLoggingEventIds.DeliverySucceeded.Should().Be(15001);
        WebhooksLoggingEventIds.DeliveryFailed.Should().Be(15002);
        WebhooksLoggingEventIds.DeliveryExhausted.Should().Be(15003);
        WebhooksLoggingEventIds.ExhaustionEventNotPublished.Should().Be(15004);
        WebhooksLoggingEventIds.DeliveryFaulted.Should().Be(15005);
        WebhooksLoggingEventIds.ExhaustionPublisherFaulted.Should().Be(15006);

        int[] ids =
        [
            .. typeof(WebhookDispatcher).Assembly.GetTypes()
                .SelectMany(t => t.GetMethods(BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                .Select(m => m.GetCustomAttribute<LoggerMessageAttribute>())
                .OfType<LoggerMessageAttribute>()
                .Select(a => a.EventId)
                .Order(),
        ];
        ids.Should().Equal(15000, 15001, 15002, 15003, 15004, 15005, 15006);
        ids.Should().OnlyContain(id => LoggingEventIdRanges.IsInSubBlock(id, LoggingEventIdRanges.Integration, 0));
    }

    [Fact]
    public async Task Every_event_has_its_level_template_and_the_delivery_id_and_follows_the_log_conventions()
    {
        var records = new List<LogRecord>();

        // 15001 success, 15002 failed with attempts left (400 is not retried), 15003 exhausted, 15004 refused exhaustion.
        var failedSubscription = Subscription();
        using (var harness = new WebhookTestHarness(
            new StubHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.BadRequest)),
            new FakeWebhookSubscriptionStore([failedSubscription]),
            o => o.MaxAttempts = 3))
        {
            await harness.Dispatcher.DispatchToSubscriptionAsync(failedSubscription, NewEvent(), CancellationToken.None);
            records.AddRange(harness.LoggerFactory.GetLogger(DispatcherCategory).Records);
        }

        var okSubscription = Subscription();
        WebhookDeliveryResult ok;
        using (var harness = new WebhookTestHarness(
            new StubHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK)),
            new FakeWebhookSubscriptionStore([okSubscription]),
            o => o.MaxAttempts = 1))
        {
            ok = await harness.Dispatcher.DispatchToSubscriptionAsync(okSubscription, NewEvent(), CancellationToken.None);
            records.AddRange(harness.LoggerFactory.GetLogger(DispatcherCategory).Records);
        }

        var exhausted = Subscription();
        using (var harness = new WebhookTestHarness(
            new StubHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)),
            new FakeWebhookSubscriptionStore([exhausted]),
            o =>
            {
                o.MaxAttempts = 2;
                o.BaseBackoffDelay = TimeSpan.FromMilliseconds(1);
                o.MaxBackoffDelay = TimeSpan.FromMilliseconds(5);
            },
            services => services.AddSingleton<SharedKernel.Messaging.Abstractions.EventPublisher.IEventPublisher>(new RefusingPublisher())))
        {
            await harness.Dispatcher.DispatchToSubscriptionAsync(exhausted, NewEvent(), CancellationToken.None);
            records.AddRange(harness.LoggerFactory.GetLogger(DispatcherCategory).Records);
        }

        var fault = new InvalidOperationException(Sentinel);
        var faulting = Subscription();
        using (var harness = new WebhookTestHarness(
            new StubHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK)),
            new FakeWebhookSubscriptionStore([faulting]),
            o => o.MaxAttempts = 1,
            services =>
            {
                services.AddSingleton<IWebhookUrlValidator>(new ThrowingValidator());
                services.AddScoped<SharedKernel.Integration.Webhooks.Observability.IWebhookDeliveryObserver>(_ => new ThrowingObserver(fault));
            }))
        {
            await harness.Dispatcher.DispatchToSubscriptionAsync(faulting, NewEvent(), CancellationToken.None);
            records.AddRange(harness.LoggerFactory.GetLogger(DispatcherCategory).Records);
        }

        records.Select(r => r.EventId.Id).Distinct().Order().Should().Equal(15000, 15001, 15002, 15003, 15004, 15005);
        records.ShouldFollowLogConvention().ShouldOnlyLogEventIdsInSubBlock(LoggingEventIdRanges.Integration, 0);

        var succeeded = records.ShouldHaveLogged(Id(1), LogLevel.Debug);
        succeeded.Message.Should().Contain($"'{ok.DeliveryId}'").And.Contain($"'{okSubscription.SubscriptionId}'");
        records.ShouldHaveLogged(Id(2), LogLevel.Warning).Message.Should().Contain("Unexpected").And.Contain(WebhookErrorCodes.EndpointRejected);
        records.ShouldHaveLogged(Id(3), LogLevel.Warning).Message.Should().Contain("Unavailable").And.Contain(WebhookErrorCodes.EndpointUnavailable);
        records.ShouldHaveLogged(Id(4), LogLevel.Warning);
        records.ShouldHaveLoggedWithException(Id(5), typeof(InvalidOperationException)).LogLevel.Should().Be(LogLevel.Error);
        records.ShouldHaveLogged(Id(0), LogLevel.Error).Exception.Should().BeSameAs(fault);
        records.Select(r => r with { Exception = null }).ToList().ShouldNotLeak(Sentinel, Secret, "example.test");
    }

    [Fact]
    public async Task The_dispatcher_logs_under_its_own_type_as_the_category()
    {
        using var handler = new StubHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK));
        var subscription = Subscription();
        using var harness = new WebhookTestHarness(handler, new FakeWebhookSubscriptionStore([subscription]), o => o.MaxAttempts = 1);

        await harness.Dispatcher.DispatchToSubscriptionAsync(subscription, NewEvent(), CancellationToken.None);

        harness.LoggerFactory.ShouldHaveCategory(typeof(WebhookDispatcher));
    }

    // --- Helpers ----------------------------------------------------------------------------------------------------

    private static Activity DeliverSpan(ActivityRecorder spans, WebhookSubscription subscription) =>
        spans.RecordedActivities
            .Where(a => a.OperationName == "webhook deliver")
            .Single(a => a.GetTagItem(WebhookTagKeys.SubscriptionId) is Guid id && id == subscription.SubscriptionId);

    [IntegrationEvent(EventName)]
    private sealed record ConformanceEvent(Guid EventId, DateTimeOffset OccurredOn) : IIntegrationEvent
    {
        public const string EventName = "tests.webhooks.conformance";
    }

    private sealed class ThrowingValidator : IWebhookUrlValidator
    {
        public Task<bool> ValidateAsync(Uri url, CancellationToken ct) => throw new InvalidOperationException(Sentinel);
    }

    private sealed class ThrowingObserver(Exception fault) : SharedKernel.Integration.Webhooks.Observability.IWebhookDeliveryObserver
    {
        public Task OnAttemptAsync(WebhookSubscription subscription, int attemptNumber, CancellationToken ct) => throw fault;

        public Task OnCompletedAsync(WebhookSubscription subscription, WebhookDeliveryResult result, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class RefusingPublisher : SharedKernel.Messaging.Abstractions.EventPublisher.IEventPublisher
    {
        public Task<SharedKernel.Primitives.Results.Result> PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken ct)
            where TEvent : class, IIntegrationEvent =>
            Task.FromResult(SharedKernel.Primitives.Results.Result.Failure(Error.Unavailable("messaging.unavailable", "The broker is unavailable.")));

        public Task<SharedKernel.Primitives.Results.Result> PublishAsync<TEvent>(
            TEvent integrationEvent,
            Action<SharedKernel.Messaging.Abstractions.EventPublisher.PublishContext> configure,
            CancellationToken ct)
            where TEvent : class, IIntegrationEvent =>
            PublishAsync(integrationEvent, ct);
    }
}

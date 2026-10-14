using System.Diagnostics;
using System.Net;
using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Polly.CircuitBreaker;
using Polly.Timeout;
using SharedKernel.Integration.Notifications.Abstractions.Delivery;
using SharedKernel.Integration.Notifications.Abstractions.Notifications;
using SharedKernel.Integration.Notifications.Abstractions.Observability;
using SharedKernel.Integration.Notifications.Abstractions.Tracing;
using SharedKernel.Integration.Notifications.Sms.Twilio.Diagnostics;
using SharedKernel.Integration.Notifications.Sms.Twilio.Tests.TestSupport;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Logging;
using SharedKernel.Primitives.Propagation;
using SharedKernel.Testing.Logging;
using SharedKernel.Testing.Telemetry;

namespace SharedKernel.Integration.Notifications.Sms.Twilio.Tests;

/// <summary>
/// The observability and error-code conformance of the Twilio provider: every failure maps to its code and
/// <see cref="ErrorType"/>, spans and instruments follow the platform conventions and carry no recipient, and the log
/// events keep their ids, levels and templates. One xUnit collection: the instruments are process-wide by name.
/// </summary>
[Collection(TwilioSenderCollection.Name)]
public sealed class TwilioConformanceTests
{
    private const string Sentinel = "https://api.sentinel-provider.invalid/2010-04-01/Accounts/ACsentinel/Messages.json";
    private const string Recipient = "+15555550123";
    private const string Code = "778899";

    private sealed record Model(string Code);

    private static NotificationMessage<Model> Message() => new()
    {
        NotificationDeliveryId = Guid.NewGuid(),
        Channel = NotificationChannel.Sms,
        Recipient = Recipient,
        TemplateId = "HXtemplate",
        TemplateModel = new Model(Code),
    };

    private static HttpResponseMessage Created() =>
        new(HttpStatusCode.Created) { Content = new StringContent("""{"sid":"SM1","status":"queued"}""") };

    private static EventId Id(int offset) => new(LoggingEventIdRanges.Integration + offset);

    private static List<LogRecord> AllRecords(TwilioTestHarness harness)
    {
        var all = new List<LogRecord>();
        foreach (var (_, logger) in harness.LoggerFactory.Loggers.Where(l => l.Key.StartsWith("SharedKernel.Integration", StringComparison.Ordinal)))
        {
            all.AddRange(logger.Records);
        }

        return all;
    }

    private static Activity SendSpan(ActivityRecorder spans, Guid deliveryId) =>
        spans.RecordedActivities
            .Where(a => a.OperationName == "notification send")
            .Single(a => a.GetTagItem(NotificationTagKeys.DeliveryId) is Guid id && id == deliveryId);

    // --- Error codes ------------------------------------------------------------------------------------------------

    public static TheoryData<string, HttpStatusCode, string, ErrorType> StatusCodes => new()
    {
        { "500", HttpStatusCode.InternalServerError, NotificationErrorCodes.ProviderUnavailable, ErrorType.Unavailable },
        { "503", HttpStatusCode.ServiceUnavailable, NotificationErrorCodes.ProviderUnavailable, ErrorType.Unavailable },
        { "429", HttpStatusCode.TooManyRequests, NotificationErrorCodes.ProviderUnavailable, ErrorType.Unavailable },
        { "400", HttpStatusCode.BadRequest, NotificationErrorCodes.ProviderRejected, ErrorType.Unexpected },
        { "401", HttpStatusCode.Unauthorized, NotificationErrorCodes.ProviderRejected, ErrorType.Unexpected },
        { "404", HttpStatusCode.NotFound, NotificationErrorCodes.ProviderRejected, ErrorType.Unexpected },
    };

    [Theory]
    [MemberData(nameof(StatusCodes))]
    public async Task A_non_success_status_maps_to_its_code_and_error_type(string name, HttpStatusCode status, string code, ErrorType type)
    {
        _ = name;
        using var handler = new StubHttpMessageHandler((_, _) => new HttpResponseMessage(status));
        using var harness = new TwilioTestHarness(handler, o => o.MaxAttempts = 1);

        var result = await harness.Sender.SendAsync(Message(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(code);
        result.Error.Type.Should().Be(type);
        result.Error.Message.Should().Be(NotificationErrors.ForStatusCode((int)status).Message).And.Contain(((int)status).ToString());
    }

    public static TheoryData<string, Func<Exception>, string, ErrorType> TransportFaults => new()
    {
        { "HttpRequestException", () => new HttpRequestException(Sentinel), NotificationErrorCodes.ProviderUnavailable, ErrorType.Unavailable },
        { "BrokenCircuitException", () => new BrokenCircuitException(Sentinel), NotificationErrorCodes.ProviderUnavailable, ErrorType.Unavailable },
        { "TimeoutRejectedException", () => new TimeoutRejectedException(Sentinel), NotificationErrorCodes.ProviderTimeout, ErrorType.Timeout },
        { "TimeoutException", () => new TimeoutException(Sentinel), NotificationErrorCodes.ProviderTimeout, ErrorType.Timeout },
        { "client timeout (TaskCanceledException)", () => new TaskCanceledException(Sentinel), NotificationErrorCodes.ProviderTimeout, ErrorType.Timeout },
    };

    [Theory]
    [MemberData(nameof(TransportFaults))]
    public async Task A_transport_failure_maps_to_its_code_and_error_type_with_a_fixed_message(string name, Func<Exception> fault, string code, ErrorType type)
    {
        _ = name;
        var thrown = fault();
        using var handler = new StubHttpMessageHandler((_, _) => throw thrown);
        using var harness = new TwilioTestHarness(handler, o => o.MaxAttempts = 1);

        var result = await harness.Sender.SendAsync(Message(), CancellationToken.None);

        result.Error!.Code.Should().Be(code);
        result.Error.Type.Should().Be(type);
        result.Error.Message.Should().NotContain("sentinel");
    }

    [Fact]
    public async Task A_result_carries_an_error_exactly_when_it_failed()
    {
        using var ok = new StubHttpMessageHandler((_, _) => Created());
        using var harness = new TwilioTestHarness(ok, o => o.MaxAttempts = 1);

        var success = await harness.Sender.SendAsync(Message(), CancellationToken.None);

        success.IsSuccess.Should().BeTrue();
        success.Error.Should().BeNull();
        success.ProviderMessageId.Should().Be("SM1");
    }

    // --- Spans ------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_successful_send_records_a_client_span_with_the_exact_tag_set_and_no_recipient()
    {
        using var spans = ActivityRecorder.StartRecording(NotificationIntegrationActivitySource.Name);
        using var handler = new StubHttpMessageHandler((_, _) => Created());
        using var harness = new TwilioTestHarness(handler, o => o.MaxAttempts = 1);
        var message = Message();

        await harness.Sender.SendAsync(message, CancellationToken.None);

        var span = SendSpan(spans, message.NotificationDeliveryId);
        span.ShouldFollowSpanConvention();
        span.ShouldHaveKind(ActivityKind.Client);
        span.ShouldHaveTagKeys(
            NotificationTagKeys.Channel,
            NotificationTagKeys.DeliveryId,
            NotificationTagKeys.AttemptCount,
            WellKnownTagKeys.Outcome);
        span.ShouldHaveTag(NotificationTagKeys.Channel, "Sms");
        span.ShouldHaveOutcome(WellKnownOutcomes.Success);
        span.ShouldBeRecordedSuccess();
        span.ShouldNotLeak(Recipient, Code, "test-auth-token");
    }

    [Fact]
    public async Task A_failed_result_is_recorded_as_a_failure_on_the_span()
    {
        using var spans = ActivityRecorder.StartRecording(NotificationIntegrationActivitySource.Name);
        using var handler = new StubHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        using var harness = new TwilioTestHarness(handler, o => o.MaxAttempts = 1);
        var message = Message();

        var result = await harness.Sender.SendAsync(message, CancellationToken.None);

        var span = SendSpan(spans, message.NotificationDeliveryId);
        span.ShouldBeRecordedFailure(result.Error!);
        span.ShouldHaveOutcome(WellKnownOutcomes.Failure);
        span.ShouldNotLeak(Recipient, Code);
    }

    [Fact]
    public async Task Caller_cancellation_propagates_and_is_not_an_error_on_the_span()
    {
        using var spans = ActivityRecorder.StartRecording(NotificationIntegrationActivitySource.Name);
        using var cts = new CancellationTokenSource();
        using var handler = new StubHttpMessageHandler((_, _) =>
        {
            cts.Cancel();
            throw new OperationCanceledException(cts.Token);
        });
        using var harness = new TwilioTestHarness(handler, o => o.MaxAttempts = 1);
        var message = Message();

        var act = () => harness.Sender.SendAsync(message, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        var span = SendSpan(spans, message.NotificationDeliveryId);
        span.ShouldBeRecordedCancellation();
        span.ShouldHaveOutcome(WellKnownOutcomes.Canceled);
    }

    // --- Instruments -------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Each_terminal_outcome_is_counted_once_with_the_channel_and_outcome_tags_only()
    {
        using var metrics = MetricRecorder.StartRecording(NotificationMetrics.MeterName);
        using var cts = new CancellationTokenSource();
        var ok = new StubHttpMessageHandler((_, _) => Created());
        var bad = new StubHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.BadRequest));
        var cancelling = new StubHttpMessageHandler((_, _) =>
        {
            cts.Cancel();
            throw new OperationCanceledException(cts.Token);
        });
        var message = Message();

        using (var harness = new TwilioTestHarness(ok, o => o.MaxAttempts = 1))
        {
            await harness.Sender.SendAsync(message, CancellationToken.None);
        }

        using (var harness = new TwilioTestHarness(bad, o => o.MaxAttempts = 1))
        {
            await harness.Sender.SendAsync(message, CancellationToken.None);
        }

        using (var harness = new TwilioTestHarness(cancelling, o => o.MaxAttempts = 1))
        {
            var act = () => harness.Sender.SendAsync(message, cts.Token);
            await act.Should().ThrowAsync<OperationCanceledException>();
        }

        KeyValuePair<string, object?> sms = new(NotificationTagKeys.Channel, "Sms");
        metrics.Sum(NotificationMetrics.SendsInstrumentName, sms, new(WellKnownTagKeys.Outcome, WellKnownOutcomes.Success)).Should().Be(1);
        metrics.Sum(NotificationMetrics.SendsInstrumentName, sms, new(WellKnownTagKeys.Outcome, WellKnownOutcomes.Failure)).Should().Be(1);
        metrics.Sum(NotificationMetrics.SendsInstrumentName, sms, new(WellKnownTagKeys.Outcome, WellKnownOutcomes.Canceled)).Should().Be(1);
        metrics.Count(NotificationMetrics.SendDurationInstrumentName, sms).Should().Be(3);

        metrics.ShouldHaveInstrument(NotificationMetrics.SendsInstrumentName, "{message}", InstrumentKind.Counter);
        metrics.ShouldHaveInstrument(NotificationMetrics.SendDurationInstrumentName, "s", InstrumentKind.Histogram);
        metrics.ShouldHaveTagSet(NotificationMetrics.SendsInstrumentName, NotificationTagKeys.Channel, WellKnownTagKeys.Outcome);
        metrics.ShouldHaveTagSet(NotificationMetrics.SendDurationInstrumentName, NotificationTagKeys.Channel, WellKnownTagKeys.Outcome);
        metrics.ShouldUseOutcomeVocabulary(NotificationMetrics.SendsInstrumentName);
        metrics.ShouldNotLeak(Recipient, Code, message.NotificationDeliveryId.ToString());
        metrics.ShouldFollowMetricConvention();
    }

    // --- Logging -----------------------------------------------------------------------------------------------------

    [Fact]
    public void The_event_ids_are_pinned_and_live_in_the_twilio_sub_block()
    {
        TwilioLoggingEventIds.DeliverySucceeded.Should().Be(15300);
        TwilioLoggingEventIds.DeliveryFailed.Should().Be(15301);
        TwilioLoggingEventIds.ObserverException.Should().Be(15302);

        int[] ids =
        [
            .. typeof(TwilioSmsNotificationSender).Assembly.GetTypes()
                .SelectMany(t => t.GetMethods(BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                .Select(m => m.GetCustomAttribute<LoggerMessageAttribute>())
                .OfType<LoggerMessageAttribute>()
                .Select(a => a.EventId)
                .Order(),
        ];
        ids.Should().Equal(15300, 15301, 15302);
        ids.Should().OnlyContain(id => LoggingEventIdRanges.IsInSubBlock(id, LoggingEventIdRanges.Integration, 3));
    }

    [Fact]
    public async Task Every_event_has_its_level_template_and_the_delivery_id_and_follows_the_log_conventions()
    {
        var records = new List<LogRecord>();
        var observerFault = new InvalidOperationException(Sentinel);
        var okMessage = Message();

        using (var harness = new TwilioTestHarness(
            new StubHttpMessageHandler((_, _) => Created()),
            o => o.MaxAttempts = 1,
            services => services.AddScoped<INotificationDeliveryObserver>(_ => new ThrowingObserver(observerFault))))
        {
            await harness.Sender.SendAsync(okMessage, CancellationToken.None);
            records.AddRange(AllRecords(harness));
        }

        var failedMessage = Message();
        using (var harness = new TwilioTestHarness(new StubHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.Unauthorized)), o => o.MaxAttempts = 1))
        {
            await harness.Sender.SendAsync(failedMessage, CancellationToken.None);
            records.AddRange(AllRecords(harness));
        }

        records.Select(r => r.EventId.Id).Distinct().Order().Should().Equal(15300, 15301, 15302);
        records.ShouldFollowLogConvention().ShouldOnlyLogEventIdsInSubBlock(LoggingEventIdRanges.Integration, 3);

        records.ShouldHaveLogged(Id(300), LogLevel.Debug).Message.Should().Contain($"'{okMessage.NotificationDeliveryId}'");
        var failed = records.Single(r => r.EventId == Id(301));
        failed.LogLevel.Should().Be(LogLevel.Warning);
        failed.Message.Should().Contain($"'{failedMessage.NotificationDeliveryId}'").And.Contain("Unexpected").And.Contain(NotificationErrorCodes.ProviderRejected).And.Contain("401");
        records.ShouldHaveLoggedWithException(Id(302), typeof(InvalidOperationException)).LogLevel.Should().Be(LogLevel.Error);
        records.Select(r => r with { Exception = null }).ToList().ShouldNotLeak(Sentinel, Recipient, Code, "test-auth-token", "ACtest");
    }

    [Fact]
    public async Task The_sender_logs_under_its_own_type_as_the_category()
    {
        using var handler = new StubHttpMessageHandler((_, _) => Created());
        using var harness = new TwilioTestHarness(handler);

        await harness.Sender.SendAsync(Message(), CancellationToken.None);

        harness.LoggerFactory.ShouldHaveCategory(typeof(TwilioSmsNotificationSender));
    }

    private sealed class ThrowingObserver(Exception fault) : INotificationDeliveryObserver
    {
        public Task OnAttemptAsync(NotificationDeliveryContext context, int attemptNumber, CancellationToken ct) => throw fault;

        public Task OnCompletedAsync(NotificationDeliveryContext context, NotificationDeliveryResult result, CancellationToken ct) => Task.CompletedTask;
    }
}

/// <summary>Serializes every Twilio sender test: the notification instruments are process-wide by meter name.</summary>
[CollectionDefinition(Name)]
public sealed class TwilioSenderCollection
{
    /// <summary>The collection name.</summary>
    public const string Name = "Twilio sender";
}

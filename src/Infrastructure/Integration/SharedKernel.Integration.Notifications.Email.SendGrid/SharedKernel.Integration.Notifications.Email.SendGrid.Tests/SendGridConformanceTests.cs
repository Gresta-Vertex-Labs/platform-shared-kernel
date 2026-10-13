using System.Diagnostics;
using System.Net;
using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Polly.CircuitBreaker;
using Polly.Timeout;
using SharedKernel.Integration.Notifications.Abstractions.Delivery;
using SharedKernel.Integration.Notifications.Abstractions.Notifications;
using SharedKernel.Integration.Notifications.Abstractions.Observability;
using SharedKernel.Integration.Notifications.Abstractions.Tracing;
using SharedKernel.Integration.Notifications.Email.SendGrid.Diagnostics;
using SharedKernel.Integration.Notifications.Email.SendGrid.Tests.TestSupport;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Logging;
using SharedKernel.Primitives.Propagation;
using SharedKernel.Primitives.Results;
using SharedKernel.Storage;
using SharedKernel.Testing.Logging;
using SharedKernel.Testing.Telemetry;

namespace SharedKernel.Integration.Notifications.Email.SendGrid.Tests;

/// <summary>
/// The observability and error-code conformance of the SendGrid provider: every failure maps to its code and
/// <see cref="ErrorType"/>, spans and instruments follow the platform conventions and carry no recipient, and the log
/// events keep their ids, levels and templates. One xUnit collection: the instruments are process-wide by name.
/// </summary>
[Collection(SendGridSenderCollection.Name)]
public sealed class SendGridConformanceTests
{
    private const string Sentinel = "https://api.sentinel-provider.invalid/v3/mail/send?key=sentinel-key";
    private const string Recipient = "pii-recipient@example.test";
    private const string Pin = "pin-731904";

    private sealed record Model(string Pin);

    private static NotificationMessage<Model> Message(IReadOnlyList<NotificationAttachment>? attachments = null) => new()
    {
        NotificationDeliveryId = Guid.NewGuid(),
        Channel = NotificationChannel.Email,
        Recipient = Recipient,
        TemplateId = "d-template",
        TemplateModel = new Model(Pin),
        Attachments = attachments,
    };

    private static NotificationAttachment Attachment() => new()
    {
        FileReference = new FileReference { Store = "invoices", Key = "invoice.pdf" },
        FileName = "invoice.pdf",
    };

    private static EventId Id(int offset) => new(LoggingEventIdRanges.Integration + offset);

    private static List<LogRecord> AllRecords(SendGridTestHarness harness)
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
        using var harness = new SendGridTestHarness(handler, o => o.MaxAttempts = 1);

        var result = await harness.Sender.SendAsync(Message(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(code);
        result.Error.Type.Should().Be(type);
        result.Error.Message.Should().Be(NotificationErrors.ForStatusCode((int)status).Message).And.Contain(((int)status).ToString());
        result.ProviderMessageId.Should().BeNull();
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
        using var harness = new SendGridTestHarness(handler, o => o.MaxAttempts = 1);

        var result = await harness.Sender.SendAsync(Message(), CancellationToken.None);

        result.Error!.Code.Should().Be(code);
        result.Error.Type.Should().Be(type);
        result.Error.Message.Should().NotContain("sentinel");
    }

    [Fact]
    public async Task An_unresolvable_attachment_is_a_validation_error_with_the_renamed_code()
    {
        using var handler = new StubHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.Accepted));
        using var withoutStorage = new SendGridTestHarness(handler, withStorage: false);
        using var withStorage = new SendGridTestHarness(handler);

        var noStorage = await withoutStorage.Sender.SendAsync(Message([Attachment()]), CancellationToken.None);
        var missing = await withStorage.Sender.SendAsync(Message([Attachment()]), CancellationToken.None);

        foreach (var result in new[] { noStorage, missing })
        {
            result.Error.Should().Be(NotificationErrors.AttachmentUnresolvable());
            result.Error!.Code.Should().Be("integration.notification.attachment_unresolvable");
            result.Error.Type.Should().Be(ErrorType.Validation);
        }

        handler.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task A_result_carries_an_error_exactly_when_it_failed()
    {
        using var ok = new StubHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.Accepted));
        using var harness = new SendGridTestHarness(ok, o => o.MaxAttempts = 1);

        var success = await harness.Sender.SendAsync(Message(), CancellationToken.None);

        success.IsSuccess.Should().BeTrue();
        success.Error.Should().BeNull();

        var id = Guid.NewGuid();
        var inconsistentSuccess = () => new NotificationDeliveryResult(id, true, null, NotificationErrors.ProviderTimeout());
        var inconsistentFailure = () => new NotificationDeliveryResult(id, false, null, null);
        inconsistentSuccess.Should().Throw<ArgumentException>();
        inconsistentFailure.Should().Throw<ArgumentException>();
    }

    // --- Spans ------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_successful_send_records_a_client_span_with_the_exact_tag_set_and_no_recipient()
    {
        using var spans = ActivityRecorder.StartRecording(NotificationIntegrationActivitySource.Name);
        using var handler = new StubHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.Accepted));
        using var harness = new SendGridTestHarness(handler, o => o.MaxAttempts = 1);
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
        span.ShouldHaveTag(NotificationTagKeys.Channel, "Email");
        span.ShouldHaveOutcome(WellKnownOutcomes.Success);
        span.ShouldBeRecordedSuccess();
        span.ShouldNotLeak(Recipient, Pin, "test-api-key");
    }

    [Fact]
    public async Task A_failed_result_is_recorded_as_a_failure_on_the_span()
    {
        using var spans = ActivityRecorder.StartRecording(NotificationIntegrationActivitySource.Name);
        using var handler = new StubHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        using var harness = new SendGridTestHarness(handler, o => o.MaxAttempts = 1);
        var message = Message();

        var result = await harness.Sender.SendAsync(message, CancellationToken.None);

        var span = SendSpan(spans, message.NotificationDeliveryId);
        span.ShouldBeRecordedFailure(result.Error!);
        span.ShouldHaveOutcome(WellKnownOutcomes.Failure);
        span.ShouldNotLeak(Recipient, Pin);
    }

    [Fact]
    public async Task An_unexpected_exception_propagates_and_is_recorded_in_full_on_the_span()
    {
        using var spans = ActivityRecorder.StartRecording(NotificationIntegrationActivitySource.Name);
        var resolver = Substitute.For<INotificationSenderIdentityResolver>();
        resolver.ResolveAsync(Arg.Any<NotificationChannel>(), Arg.Any<CancellationToken>()).Returns<Task<NotificationSenderIdentity>>(_ => throw new InvalidOperationException("resolver-fault"));
        using var handler = new StubHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.Accepted));
        using var harness = new SendGridTestHarness(handler, o => o.MaxAttempts = 1, services => services.AddSingleton(resolver));
        var message = Message();

        var act = () => harness.Sender.SendAsync(message, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        var span = SendSpan(spans, message.NotificationDeliveryId);
        span.ShouldBeRecordedException(typeof(InvalidOperationException));
        span.ShouldHaveOutcome(WellKnownOutcomes.Exception);
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
        using var harness = new SendGridTestHarness(handler, o => o.MaxAttempts = 1);
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
        var ok = new StubHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.Accepted));
        var bad = new StubHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.BadRequest));
        var cancelling = new StubHttpMessageHandler((_, _) =>
        {
            cts.Cancel();
            throw new OperationCanceledException(cts.Token);
        });
        var message = Message();

        using (var harness = new SendGridTestHarness(ok, o => o.MaxAttempts = 1))
        {
            await harness.Sender.SendAsync(message, CancellationToken.None);
        }

        using (var harness = new SendGridTestHarness(bad, o => o.MaxAttempts = 1))
        {
            await harness.Sender.SendAsync(message, CancellationToken.None);
        }

        using (var harness = new SendGridTestHarness(cancelling, o => o.MaxAttempts = 1))
        {
            var act = () => harness.Sender.SendAsync(message, cts.Token);
            await act.Should().ThrowAsync<OperationCanceledException>();
        }

        KeyValuePair<string, object?> email = new(NotificationTagKeys.Channel, "Email");
        metrics.Sum(NotificationMetrics.SendsInstrumentName, email, new(WellKnownTagKeys.Outcome, WellKnownOutcomes.Success)).Should().Be(1);
        metrics.Sum(NotificationMetrics.SendsInstrumentName, email, new(WellKnownTagKeys.Outcome, WellKnownOutcomes.Failure)).Should().Be(1);
        metrics.Sum(NotificationMetrics.SendsInstrumentName, email, new(WellKnownTagKeys.Outcome, WellKnownOutcomes.Canceled)).Should().Be(1);
        metrics.Count(NotificationMetrics.SendDurationInstrumentName, email).Should().Be(3);

        metrics.ShouldHaveInstrument(NotificationMetrics.SendsInstrumentName, "{message}", InstrumentKind.Counter);
        metrics.ShouldHaveInstrument(NotificationMetrics.SendDurationInstrumentName, "s", InstrumentKind.Histogram);
        metrics.ShouldHaveTagSet(NotificationMetrics.SendsInstrumentName, NotificationTagKeys.Channel, WellKnownTagKeys.Outcome);
        metrics.ShouldHaveTagSet(NotificationMetrics.SendDurationInstrumentName, NotificationTagKeys.Channel, WellKnownTagKeys.Outcome);
        metrics.ShouldNotHaveTagKey(NotificationMetrics.SendsInstrumentName, NotificationTagKeys.DeliveryId);
        metrics.ShouldUseOutcomeVocabulary(NotificationMetrics.SendsInstrumentName);
        metrics.ShouldNotLeak(Recipient, Pin, message.NotificationDeliveryId.ToString());
        metrics.ShouldFollowMetricConvention();
        NotificationMetrics.MeterName.Should().Be("SharedKernel.Integration").And.Be(NotificationIntegrationActivitySource.Name);
    }

    // --- Logging -----------------------------------------------------------------------------------------------------

    [Fact]
    public void The_event_ids_are_pinned_and_live_in_the_sendgrid_sub_block()
    {
        SendGridLoggingEventIds.DeliverySucceeded.Should().Be(15200);
        SendGridLoggingEventIds.DeliveryFailed.Should().Be(15201);
        SendGridLoggingEventIds.ObserverException.Should().Be(15202);
        SendGridLoggingEventIds.AttachmentUnresolvable.Should().Be(15203);

        int[] ids =
        [
            .. typeof(SendGridEmailNotificationSender).Assembly.GetTypes()
                .SelectMany(t => t.GetMethods(BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                .Select(m => m.GetCustomAttribute<LoggerMessageAttribute>())
                .OfType<LoggerMessageAttribute>()
                .Select(a => a.EventId)
                .Order(),
        ];
        ids.Should().Equal(15200, 15201, 15202, 15203);
        ids.Should().OnlyContain(id => LoggingEventIdRanges.IsInSubBlock(id, LoggingEventIdRanges.Integration, 2));
    }

    [Fact]
    public async Task Every_event_has_its_level_template_and_the_delivery_id_and_follows_the_log_conventions()
    {
        var records = new List<LogRecord>();
        var observerFault = new InvalidOperationException(Sentinel);
        var okMessage = Message();

        using (var harness = new SendGridTestHarness(
            new StubHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.Accepted)),
            o => o.MaxAttempts = 1,
            services => services.AddScoped<INotificationDeliveryObserver>(_ => new ThrowingObserver(observerFault))))
        {
            await harness.Sender.SendAsync(okMessage, CancellationToken.None);
            records.AddRange(AllRecords(harness));
        }

        var failedMessage = Message();
        using (var harness = new SendGridTestHarness(new StubHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.BadRequest)), o => o.MaxAttempts = 1))
        {
            await harness.Sender.SendAsync(failedMessage, CancellationToken.None);
            records.AddRange(AllRecords(harness));
        }

        var store = Substitute.For<IFileStorage>();
        store
            .DownloadAsync(Arg.Any<string>(), Arg.Any<FileDownloadOptions?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result<FileDownload>.Failure(Error.Unavailable("storage.unavailable", Sentinel))));
        var factory = Substitute.For<IFileStorageFactory>();
        factory.Open(Arg.Any<FileReference>()).Returns(store);
        using (var harness = new SendGridTestHarness(
            new StubHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.Accepted)),
            configureServices: services => services.AddSingleton(factory)))
        {
            await harness.Sender.SendAsync(Message([Attachment()]), CancellationToken.None);
            records.AddRange(AllRecords(harness));
        }

        records.Select(r => r.EventId.Id).Distinct().Order().Should().Equal(15200, 15201, 15202, 15203);
        records.ShouldFollowLogConvention().ShouldOnlyLogEventIdsInSubBlock(LoggingEventIdRanges.Integration, 2);

        records.ShouldHaveLogged(Id(200), LogLevel.Debug).Message.Should().Contain($"'{okMessage.NotificationDeliveryId}'");
        var failed = records.First(r => r.EventId == Id(201) && r.Message.Contains(failedMessage.NotificationDeliveryId.ToString(), StringComparison.Ordinal));
        failed.LogLevel.Should().Be(LogLevel.Warning);
        failed.Message.Should().Contain("Unexpected").And.Contain(NotificationErrorCodes.ProviderRejected).And.Contain("400");
        records.ShouldHaveLoggedWithException(Id(202), typeof(InvalidOperationException)).LogLevel.Should().Be(LogLevel.Error);
        records.ShouldHaveLogged(Id(203), LogLevel.Warning).Message.Should().Contain("storage.unavailable");
        records.Select(r => r with { Exception = null }).ToList().ShouldNotLeak(Sentinel, Recipient, Pin, "test-api-key");
    }

    [Fact]
    public async Task The_sender_logs_under_its_own_type_as_the_category()
    {
        using var handler = new StubHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.Accepted));
        using var harness = new SendGridTestHarness(handler);

        await harness.Sender.SendAsync(Message(), CancellationToken.None);

        harness.LoggerFactory.ShouldHaveCategory(typeof(SendGridEmailNotificationSender));
    }

    private sealed class ThrowingObserver(Exception fault) : INotificationDeliveryObserver
    {
        public Task OnAttemptAsync(NotificationDeliveryContext context, int attemptNumber, CancellationToken ct) => throw fault;

        public Task OnCompletedAsync(NotificationDeliveryContext context, NotificationDeliveryResult result, CancellationToken ct) => Task.CompletedTask;
    }
}

/// <summary>Serializes every SendGrid sender test: the notification instruments are process-wide by meter name.</summary>
[CollectionDefinition(Name)]
public sealed class SendGridSenderCollection
{
    /// <summary>The collection name.</summary>
    public const string Name = "SendGrid sender";
}

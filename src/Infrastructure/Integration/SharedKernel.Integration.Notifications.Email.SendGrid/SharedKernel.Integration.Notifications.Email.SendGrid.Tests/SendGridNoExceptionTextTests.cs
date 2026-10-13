using SharedKernel.Integration.Notifications.Abstractions.Delivery;
using System.Net;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Polly.CircuitBreaker;
using Polly.Timeout;
using SharedKernel.Integration.Notifications.Abstractions.Notifications;
using SharedKernel.Integration.Notifications.Abstractions.Tracing;
using SharedKernel.Integration.Notifications.Email.SendGrid.Tests.TestSupport;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Logging;
using SharedKernel.Primitives.Results;
using SharedKernel.Storage;
using SharedKernel.Testing.Logging;
using SharedKernel.Testing.Telemetry;

namespace SharedKernel.Integration.Notifications.Email.SendGrid.Tests;

/// <summary>
/// Rule 24: exception text (a provider endpoint, a storage message) never reaches a <see cref="Abstractions.Delivery.NotificationDeliveryResult"/>,
/// a log record or a span. The failure log carries the closed failure kind and the exception object; recipient and
/// template fields stay absent from every record.
/// </summary>
[Collection(SendGridSenderCollection.Name)]
public sealed class SendGridNoExceptionTextTests
{
    private const string Sentinel = "https://api.sentinel-provider.invalid/v3/mail/send?key=sentinel-key";
    private const string Recipient = "pii-recipient@example.test";
    private const string Pin = "pin-731904";
    private static readonly EventId DeliveryFailedId = new(LoggingEventIdRanges.Integration + 201);

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

    private static List<LogRecord> AllRecords(SendGridTestHarness harness)
    {
        var all = new List<LogRecord>();
        // Only this package's own categories: the resilience handler's Polly telemetry is a third-party logger.
        foreach (var (_, logger) in harness.LoggerFactory.Loggers.Where(l => l.Key.StartsWith("SharedKernel.Integration", StringComparison.Ordinal)))
        {
            all.AddRange(logger.Records);
        }

        return all;
    }

    private static void AssertNoLeak(SendGridTestHarness harness, ActivityRecorder spans, Guid deliveryId, params string[] forbidden)
    {
        var records = AllRecords(harness);
        records.Select(r => r with { Exception = null }).ToList().ShouldNotLeak([.. forbidden, Recipient, Pin]);

        spans.RecordedActivities
            .Where(a => a.OperationName == "notification send")
            .ShouldNotLeak([.. forbidden, Recipient, Pin]);
        _ = deliveryId;
    }

    public static TheoryData<string, Func<Exception>, string> TransportFaults => new()
    {
        { "HttpRequestException", () => new HttpRequestException(Sentinel), NotificationErrorCodes.ProviderUnavailable },
        { "TimeoutRejectedException", () => new TimeoutRejectedException(Sentinel), NotificationErrorCodes.ProviderTimeout },
        { "BrokenCircuitException", () => new BrokenCircuitException(Sentinel), NotificationErrorCodes.ProviderUnavailable },
    };

    [Theory]
    [MemberData(nameof(TransportFaults))]
    public async Task SendAsync_TransportFault_ResultLogAndSpanCarryNoExceptionText(string name, Func<Exception> createFault, string expectedReason)
    {
        _ = name;
        var fault = createFault();
        using var spans = ActivityRecorder.StartRecording(NotificationIntegrationActivitySource.Name);
        using var handler = new StubHttpMessageHandler((_, _) => throw fault);
        using var harness = new SendGridTestHarness(handler, o => o.MaxAttempts = 1);
        var message = Message();

        var result = await harness.Sender.SendAsync(message, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(expectedReason);
        AssertNoLeak(harness, spans, message.NotificationDeliveryId, Sentinel, "sentinel-provider", "sentinel-key");

        var record = AllRecords(harness).ShouldHaveLoggedWithException(DeliveryFailedId, fault.GetType());
        record.Exception.Should().BeSameAs(fault);
        record.LogLevel.Should().Be(LogLevel.Warning);
    }

    [Fact]
    public async Task SendAsync_StoreOpenThrows_ResultAndLogsCarryNoExceptionText()
    {
        var fault = new InvalidOperationException(Sentinel);
        var factory = Substitute.For<IFileStorageFactory>();
        factory.Open(Arg.Any<FileReference>()).Returns(_ => throw fault);

        using var spans = ActivityRecorder.StartRecording(NotificationIntegrationActivitySource.Name);
        using var handler = new StubHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.Accepted));
        using var harness = new SendGridTestHarness(handler, configureServices: s => s.AddSingleton(factory));
        var message = Message([Attachment()]);

        var result = await harness.Sender.SendAsync(message, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(NotificationErrors.AttachmentUnresolvable());
        handler.CallCount.Should().Be(0);
        AssertNoLeak(harness, spans, message.NotificationDeliveryId, Sentinel);
        AllRecords(harness).ShouldHaveLoggedWithException(DeliveryFailedId, typeof(InvalidOperationException)).Exception.Should().BeSameAs(fault);
    }

    [Fact]
    public async Task SendAsync_StorageDownloadFails_StorageErrorMessageNeverReachesResultOrLogs()
    {
        var store = Substitute.For<IFileStorage>();
        store
            .DownloadAsync(Arg.Any<string>(), Arg.Any<FileDownloadOptions?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result<FileDownload>.Failure(Error.Unavailable("storage.unavailable", Sentinel))));
        var factory = Substitute.For<IFileStorageFactory>();
        factory.Open(Arg.Any<FileReference>()).Returns(store);

        using var spans = ActivityRecorder.StartRecording(NotificationIntegrationActivitySource.Name);
        using var handler = new StubHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.Accepted));
        using var harness = new SendGridTestHarness(handler, configureServices: s => s.AddSingleton(factory));
        var message = Message([Attachment()]);

        var result = await harness.Sender.SendAsync(message, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(NotificationErrors.AttachmentUnresolvable());
        handler.CallCount.Should().Be(0);
        AssertNoLeak(harness, spans, message.NotificationDeliveryId, Sentinel);
        AllRecords(harness).ShouldHaveLogged(DeliveryFailedId, LogLevel.Warning);
    }

    [Fact]
    public async Task SendAsync_NonSuccessStatus_FailureLogCarriesKindAndStatusCodeOnly()
    {
        using var spans = ActivityRecorder.StartRecording(NotificationIntegrationActivitySource.Name);
        using var handler = new StubHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            ReasonPhrase = "reason-phrase-sentinel",
            Content = new StringContent("body-sentinel"),
        });
        using var harness = new SendGridTestHarness(handler, o => o.MaxAttempts = 1);
        var message = Message();

        var result = await harness.Sender.SendAsync(message, CancellationToken.None);

        result.Error.Should().Be(NotificationErrors.ProviderRejected(400));
        AssertNoLeak(harness, spans, message.NotificationDeliveryId, "reason-phrase-sentinel", "body-sentinel");
        var record = AllRecords(harness).ShouldHaveLogged(DeliveryFailedId, LogLevel.Warning);
        record.TryGetProperty("ErrorCode", out var kind).Should().BeTrue();
        kind.Should().Be(NotificationErrorCodes.ProviderRejected);
        record.TryGetProperty("StatusCode", out var status).Should().BeTrue();
        status.Should().Be(400);
    }
}

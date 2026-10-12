using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Polly.CircuitBreaker;
using Polly.Timeout;
using SharedKernel.Integration.Notifications.Abstractions.Notifications;
using SharedKernel.Integration.Notifications.Abstractions.Tracing;
using SharedKernel.Integration.Notifications.Sms.Twilio.Tests.TestSupport;
using SharedKernel.Primitives.Logging;
using SharedKernel.Testing.Logging;
using SharedKernel.Testing.Telemetry;

namespace SharedKernel.Integration.Notifications.Sms.Twilio.Tests;

/// <summary>
/// Rule 24: exception text (the provider endpoint) never reaches a result, a log record or a span. The failure log
/// carries the closed failure kind and the exception object; recipient and template fields stay absent.
/// </summary>
public sealed class TwilioNoExceptionTextTests
{
    private const string Sentinel = "https://api.sentinel-provider.invalid/2010-04-01/Accounts/ACsentinel/Messages.json";
    private const string Recipient = "+15555550999";
    private const string Code = "otp-482913";
    private static readonly EventId DeliveryFailedId = new(LoggingEventIdRanges.Integration + 301);

    private sealed record Model(string Code);

    private static NotificationMessage<Model> Message() => new()
    {
        NotificationDeliveryId = Guid.NewGuid(),
        Channel = NotificationChannel.Sms,
        Recipient = Recipient,
        TemplateId = "HXtemplate",
        TemplateModel = new Model(Code),
    };

    private static List<LogRecord> AllRecords(TwilioTestHarness harness)
    {
        var all = new List<LogRecord>();
        // Only this package's own categories: the resilience handler's Polly telemetry is a third-party logger.
        foreach (var (_, logger) in harness.LoggerFactory.Loggers.Where(l => l.Key.StartsWith("SharedKernel.Integration", StringComparison.Ordinal)))
        {
            all.AddRange(logger.Records);
        }

        return all;
    }

    private static void AssertNoLeak(TwilioTestHarness harness, ActivityRecorder spans, params string[] forbidden)
    {
        AllRecords(harness).Select(r => r with { Exception = null }).ToList().ShouldNotLeak([.. forbidden, Recipient, Code]);
        spans.RecordedActivities
            .Where(a => a.OperationName == "NotificationSender.Send")
            .ShouldNotLeak([.. forbidden, Recipient, Code]);
    }

    public static TheoryData<string, Func<Exception>, string> TransportFaults => new()
    {
        { "HttpRequestException", () => new HttpRequestException(Sentinel), "Twilio could not be reached." },
        { "TimeoutRejectedException", () => new TimeoutRejectedException(Sentinel), "Twilio delivery timed out." },
        { "BrokenCircuitException", () => new BrokenCircuitException(Sentinel), "Twilio delivery was rejected by the resilience pipeline." },
    };

    [Theory]
    [MemberData(nameof(TransportFaults))]
    public async Task SendAsync_TransportFault_ResultLogAndSpanCarryNoExceptionText(string name, Func<Exception> createFault, string expectedReason)
    {
        _ = name;
        var fault = createFault();
        using var spans = ActivityRecorder.StartRecording(NotificationIntegrationActivitySource.Name);
        using var handler = new StubHttpMessageHandler((_, _) => throw fault);
        using var harness = new TwilioTestHarness(handler, o => o.MaxAttempts = 1);

        var result = await harness.Sender.SendAsync(Message(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(expectedReason);
        AssertNoLeak(harness, spans, Sentinel, "sentinel-provider", "ACsentinel");

        var record = AllRecords(harness).ShouldHaveLoggedWithException(DeliveryFailedId, fault.GetType());
        record.Exception.Should().BeSameAs(fault);
        record.LogLevel.Should().Be(LogLevel.Warning);
    }

    [Fact]
    public async Task SendAsync_NonSuccessStatus_FailureLogCarriesKindAndStatusCodeOnly()
    {
        using var spans = ActivityRecorder.StartRecording(NotificationIntegrationActivitySource.Name);
        using var handler = new StubHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            ReasonPhrase = "reason-phrase-sentinel",
            Content = new StringContent("body-sentinel"),
        });
        using var harness = new TwilioTestHarness(handler, o => o.MaxAttempts = 1);

        var result = await harness.Sender.SendAsync(Message(), CancellationToken.None);

        result.Error.Should().Be("Twilio responded with status code 401.");
        AssertNoLeak(harness, spans, "reason-phrase-sentinel", "body-sentinel");
        var record = AllRecords(harness).ShouldHaveLogged(DeliveryFailedId, LogLevel.Warning);
        record.TryGetProperty("FailureKind", out var kind).Should().BeTrue();
        kind.Should().Be("provider_rejected");
        record.TryGetProperty("StatusCode", out var status).Should().BeTrue();
        status.Should().Be(401);
    }
}

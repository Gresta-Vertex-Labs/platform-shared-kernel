using System.Net;
using System.Web;
using FluentAssertions;
using SharedKernel.Integration.Notifications.Abstractions.Notifications;
using SharedKernel.Integration.Notifications.Sms.Twilio.Tests.TestSupport;
using SharedKernel.Primitives.Logging;

namespace SharedKernel.Integration.Notifications.Sms.Twilio.Tests;

public sealed class TwilioSmsNotificationSenderTests
{
    private sealed record OtpTemplateModel(string Code);

    private const string RecipientPhoneNumber = "+15555550123";
    private const string OtpCode = "778899";

    private static NotificationMessage<OtpTemplateModel> Message(Guid? deliveryId = null) => new()
    {
        NotificationDeliveryId = deliveryId ?? Guid.NewGuid(),
        Channel = NotificationChannel.Sms,
        Recipient = RecipientPhoneNumber,
        TemplateId = "HXtest0000000000000000000000001",
        TemplateModel = new OtpTemplateModel(OtpCode),
    };

    private static HttpResponseMessage TwilioSuccessResponse(string sid = "SMtest0000000000000000000000001") =>
        new(HttpStatusCode.Created)
        {
            Content = new StringContent($$"""{"sid":"{{sid}}","status":"queued"}"""),
        };

    [Fact]
    public async Task SendAsync_Success_ReturnsSuccessResultWithProviderMessageId()
    {
        var deliveryId = Guid.NewGuid();

        using var handler = new StubHttpMessageHandler((_, _) => TwilioSuccessResponse("SMabc123"));
        using var harness = new TwilioTestHarness(handler);

        var result = await harness.Sender.SendAsync(Message(deliveryId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.NotificationDeliveryId.Should().Be(deliveryId);
        result.ProviderMessageId.Should().Be("SMabc123");
    }

    [Fact]
    public async Task SendAsync_RequestBody_IsFormEncodedWithExpectedFields()
    {
        HttpRequestMessage? capturedRequest = null;
        string? capturedBody = null;

        using var handler = new StubHttpMessageHandler((request, _) =>
        {
            capturedRequest = request;
            capturedBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return TwilioSuccessResponse();
        });
        using var harness = new TwilioTestHarness(handler);

        await harness.Sender.SendAsync(Message(), CancellationToken.None);

        capturedRequest!.Content!.Headers.ContentType!.MediaType.Should().Be("application/x-www-form-urlencoded");

        var fields = HttpUtility.ParseQueryString(capturedBody!);
        fields["To"].Should().Be(RecipientPhoneNumber);
        fields["ContentSid"].Should().Be("HXtest0000000000000000000000001");
        fields["From"].Should().Be("+15005550006");

        // ContentVariables is a JSON-encoded STRING form field value, not a nested JSON object.
        var contentVariables = fields["ContentVariables"];
        contentVariables.Should().NotBeNullOrEmpty();
        contentVariables.Should().Contain("Code");
        contentVariables.Should().Contain(OtpCode);
    }

    [Fact]
    public async Task SendAsync_PropagatesNotificationDeliveryIdViaIdempotencyKeyHeader()
    {
        var deliveryId = Guid.NewGuid();
        HttpRequestMessage? capturedRequest = null;

        using var handler = new StubHttpMessageHandler((request, _) =>
        {
            capturedRequest = request;
            return TwilioSuccessResponse();
        });
        using var harness = new TwilioTestHarness(handler);

        await harness.Sender.SendAsync(Message(deliveryId), CancellationToken.None);

        capturedRequest!.Headers.GetValues(TwilioHeaders.IdempotencyKeyHeaderName).Single().Should().Be(deliveryId.ToString());
    }

    [Fact]
    public async Task SendAsync_UsesHttpBasicAuthentication()
    {
        HttpRequestMessage? capturedRequest = null;
        using var handler = new StubHttpMessageHandler((request, _) =>
        {
            capturedRequest = request;
            return TwilioSuccessResponse();
        });
        using var harness = new TwilioTestHarness(handler);

        await harness.Sender.SendAsync(Message(), CancellationToken.None);

        capturedRequest!.Headers.Authorization!.Scheme.Should().Be("Basic");
    }

    [Fact]
    public async Task SendAsync_NonSuccessResponse_ReturnsFailureWithoutThrowing()
    {
        using var handler = new StubHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        using var harness = new TwilioTestHarness(handler, o => o.MaxAttempts = 1);

        var result = await harness.Sender.SendAsync(Message(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("401");
    }

    [Fact]
    public async Task SendAsync_NullMessage_Throws()
    {
        using var handler = new StubHttpMessageHandler((_, _) => TwilioSuccessResponse());
        using var harness = new TwilioTestHarness(handler);

        var act = async () => await harness.Sender.SendAsync<OtpTemplateModel>(null!, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task SendAsync_NeverProducesAnHtmlBody()
    {
        // Structural guarantee: the Content API's ContentVariables substitution never produces
        // markup, and this sender has no separate HTML-rendering code path at all — there is no
        // "html" field/property anywhere on the outbound form body.
        string? capturedBody = null;
        using var handler = new StubHttpMessageHandler((request, _) =>
        {
            capturedBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return TwilioSuccessResponse();
        });
        using var harness = new TwilioTestHarness(handler);

        await harness.Sender.SendAsync(Message(), CancellationToken.None);

        var fields = HttpUtility.ParseQueryString(capturedBody!);
        fields.AllKeys.Should().NotContain(k => k != null && k.Contains("html", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SendAsync_NeverLogsRecipientOrTemplateModelFields()
    {
        using var handler = new StubHttpMessageHandler((_, _) => TwilioSuccessResponse());
        using var harness = new TwilioTestHarness(handler);

        await harness.Sender.SendAsync(Message(), CancellationToken.None);

        foreach (var (_, logger) in harness.LoggerFactory.Loggers)
        {
            foreach (var record in logger.Records)
            {
                record.Message.Should().NotContain(RecipientPhoneNumber);
                record.Message.Should().NotContain(OtpCode);

                if (record.State is not null)
                {
                    foreach (var kvp in record.State)
                    {
                        kvp.Value?.ToString().Should().NotContain(RecipientPhoneNumber);
                        kvp.Value?.ToString().Should().NotContain(OtpCode);
                    }
                }
            }
        }
    }

    [Fact]
    public async Task SendAsync_Success_LogsDeliverySucceeded()
    {
        using var handler = new StubHttpMessageHandler((_, _) => TwilioSuccessResponse());
        using var harness = new TwilioTestHarness(handler);

        await harness.Sender.SendAsync(Message(), CancellationToken.None);

        var logger = harness.LoggerFactory.GetLogger(typeof(TwilioSmsNotificationSender).FullName!);
        logger.Records.Should().Contain(r => r.EventId == LoggingEventIdRanges.Integration + 300);
    }

    [Fact]
    public async Task SendAsync_Failure_LogsDeliveryFailed()
    {
        using var handler = new StubHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        using var harness = new TwilioTestHarness(handler, o => o.MaxAttempts = 1);

        await harness.Sender.SendAsync(Message(), CancellationToken.None);

        var logger = harness.LoggerFactory.GetLogger(typeof(TwilioSmsNotificationSender).FullName!);
        logger.Records.Should().Contain(r => r.EventId == LoggingEventIdRanges.Integration + 301);
    }
}

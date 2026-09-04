using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using SharedKernel.Integration.Notifications.Abstractions.Notifications;
using SharedKernel.Integration.Notifications.Email.SendGrid.Tests.TestSupport;
using SharedKernel.Primitives.Logging;

namespace SharedKernel.Integration.Notifications.Email.SendGrid.Tests;

public sealed class SendGridEmailNotificationSenderTests
{
    private sealed record TestTemplateModel(string OrderNumber, string SecretPin);

    private const string RecipientEmail = "customer@example.test";
    private const string SecretPin = "941287"; // stands in for a PII-bearing template field

    private static NotificationMessage<TestTemplateModel> Message(
        Guid? deliveryId = null,
        IReadOnlyList<NotificationAttachment>? attachments = null) => new()
    {
        NotificationDeliveryId = deliveryId ?? Guid.NewGuid(),
        Channel = NotificationChannel.Email,
        Recipient = RecipientEmail,
        TemplateId = "d-order-receipt",
        TemplateModel = new TestTemplateModel("ORD-1", SecretPin),
        Attachments = attachments,
    };

    [Fact]
    public async Task SendAsync_Success_ReturnsSuccessResultAndPostsExpectedShape()
    {
        var deliveryId = Guid.NewGuid();

        JsonDocument? capturedBody = null;

        using var handler = new StubHttpMessageHandler((request, _) =>
        {
            var raw = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            capturedBody = JsonDocument.Parse(raw);

            var response = new HttpResponseMessage(HttpStatusCode.Accepted);
            response.Headers.Add("X-Message-Id", "sg-message-id-123");
            return response;
        });

        using var harness = new SendGridTestHarness(handler);

        var result = await harness.Sender.SendAsync(Message(deliveryId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.NotificationDeliveryId.Should().Be(deliveryId);
        result.ProviderMessageId.Should().Be("sg-message-id-123");
        result.Error.Should().BeNull();

        var root = capturedBody!.RootElement;
        root.GetProperty("template_id").GetString().Should().Be("d-order-receipt");
        root.GetProperty("from").GetProperty("email").GetString().Should().Be("no-reply@example.test");
        root.GetProperty("reply_to").GetProperty("email").GetString().Should().Be("support@example.test");

        var personalization = root.GetProperty("personalizations")[0];
        personalization.GetProperty("to")[0].GetProperty("email").GetString().Should().Be(RecipientEmail);
        personalization.GetProperty("dynamic_template_data").GetProperty("OrderNumber").GetString().Should().Be("ORD-1");
    }

    [Fact]
    public async Task SendAsync_PropagatesNotificationDeliveryIdViaCustomArgs()
    {
        var deliveryId = Guid.NewGuid();
        JsonDocument? capturedBody = null;

        using var handler = new StubHttpMessageHandler((request, _) =>
        {
            capturedBody = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            return new HttpResponseMessage(HttpStatusCode.Accepted);
        });

        using var harness = new SendGridTestHarness(handler);

        await harness.Sender.SendAsync(Message(deliveryId), CancellationToken.None);

        capturedBody!.RootElement.GetProperty("custom_args").GetProperty("notification_delivery_id").GetString()
            .Should().Be(deliveryId.ToString());
    }

    [Fact]
    public async Task SendAsync_WithAttachment_IncludesBase64ContentAndFilename()
    {
        var attachmentBytes = "invoice-bytes"u8.ToArray();

        JsonDocument? capturedBody = null;
        using var handler = new StubHttpMessageHandler((request, _) =>
        {
            capturedBody = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            return new HttpResponseMessage(HttpStatusCode.Accepted);
        });
        using var harness = new SendGridTestHarness(handler);
        harness.FileStorage.Seed("invoices", "invoice-1.pdf", new MemoryStream(attachmentBytes), "application/pdf");

        var attachment = new NotificationAttachment
        {
            FileReference = new SharedKernel.Storage.Abstractions.Models.FileReference { Bucket = "invoices", Key = "invoice-1.pdf" },
            FileName = "invoice.pdf",
            ContentType = "application/pdf",
        };

        var result = await harness.Sender.SendAsync(Message(attachments: [attachment]), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        var attachments = capturedBody!.RootElement.GetProperty("attachments");
        attachments.GetArrayLength().Should().Be(1);
        attachments[0].GetProperty("filename").GetString().Should().Be("invoice.pdf");
        attachments[0].GetProperty("type").GetString().Should().Be("application/pdf");

        var expectedBase64 = Convert.ToBase64String(attachmentBytes);
        attachments[0].GetProperty("content").GetString().Should().Be(expectedBase64);
    }

    [Fact]
    public async Task SendAsync_UnresolvableAttachment_ReturnsFailureWithoutThrowing()
    {
        using var handler = new StubHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.Accepted));
        using var harness = new SendGridTestHarness(handler);

        var attachment = new NotificationAttachment
        {
            FileReference = new SharedKernel.Storage.Abstractions.Models.FileReference { Bucket = "invoices", Key = "does-not-exist.pdf" },
            FileName = "invoice.pdf",
        };

        var act = async () => await harness.Sender.SendAsync(Message(attachments: [attachment]), CancellationToken.None);

        var result = await act.Should().NotThrowAsync();
        result.Subject.IsSuccess.Should().BeFalse();
        result.Subject.Error.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task SendAsync_NonSuccessResponse_ReturnsFailureWithoutThrowing()
    {
        using var handler = new StubHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.BadRequest));
        using var harness = new SendGridTestHarness(handler, o => o.MaxAttempts = 1);

        var result = await harness.Sender.SendAsync(Message(), CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("400");
    }

    [Fact]
    public async Task SendAsync_NullMessage_Throws()
    {
        using var handler = new StubHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.Accepted));
        using var harness = new SendGridTestHarness(handler);

        var act = async () => await harness.Sender.SendAsync<TestTemplateModel>(null!, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task SendAsync_NeverLogsRecipientOrTemplateModelFields()
    {
        using var handler = new StubHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.Accepted));
        using var harness = new SendGridTestHarness(handler);

        await harness.Sender.SendAsync(Message(), CancellationToken.None);

        foreach (var (_, logger) in harness.LoggerFactory.Loggers)
        {
            foreach (var record in logger.Records)
            {
                record.Message.Should().NotContain(RecipientEmail);
                record.Message.Should().NotContain(SecretPin);

                if (record.State is not null)
                {
                    foreach (var kvp in record.State)
                    {
                        kvp.Value?.ToString().Should().NotContain(RecipientEmail);
                        kvp.Value?.ToString().Should().NotContain(SecretPin);
                    }
                }
            }
        }
    }

    [Fact]
    public async Task SendAsync_Success_LogsDeliverySucceeded()
    {
        using var handler = new StubHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.Accepted));
        using var harness = new SendGridTestHarness(handler);

        await harness.Sender.SendAsync(Message(), CancellationToken.None);

        var logger = harness.LoggerFactory.GetLogger(typeof(SendGridEmailNotificationSender).FullName!);
        logger.Records.Should().Contain(r => r.EventId == LoggingEventIdRanges.Integration + 200);
    }

    [Fact]
    public async Task SendAsync_Failure_LogsDeliveryFailed()
    {
        using var handler = new StubHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.BadRequest));
        using var harness = new SendGridTestHarness(handler, o => o.MaxAttempts = 1);

        await harness.Sender.SendAsync(Message(), CancellationToken.None);

        var logger = harness.LoggerFactory.GetLogger(typeof(SendGridEmailNotificationSender).FullName!);
        logger.Records.Should().Contain(r => r.EventId == LoggingEventIdRanges.Integration + 201);
    }
}

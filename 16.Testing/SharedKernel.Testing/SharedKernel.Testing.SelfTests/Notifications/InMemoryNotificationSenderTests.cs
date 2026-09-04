using SharedKernel.Integration.Notifications.Abstractions.Delivery;
using SharedKernel.Integration.Notifications.Abstractions.Notifications;
using SharedKernel.Testing.Notifications;

namespace SharedKernel.Testing.SelfTests.Notifications;

/// <summary>
/// Proves <see cref="InMemoryNotificationSender"/> against <c>INotificationSender</c>'s documented
/// contract — no consuming domain has adopted this fake yet, so this self-test is the only
/// behavioral proof today, per the SelfTests routing rule.
/// </summary>
public sealed class InMemoryNotificationSenderTests
{
    private sealed record ReceiptModel(string CustomerName, string Amount);

    private static NotificationMessage<ReceiptModel> CreateMessage(Guid? deliveryId = null) => new()
    {
        NotificationDeliveryId = deliveryId ?? Guid.NewGuid(),
        Channel = NotificationChannel.Email,
        Recipient = "test@example.com",
        TemplateId = "receipt-template",
        TemplateModel = new ReceiptModel("Jane Doe", "42.00"),
    };

    [Fact]
    public void SupportedChannel_ReturnsConstructorValue()
    {
        var sender = new InMemoryNotificationSender(NotificationChannel.Sms);

        Assert.Equal(NotificationChannel.Sms, sender.SupportedChannel);
    }

    [Fact]
    public async Task SendAsync_RecordsMessage_ReturnsSyntheticSuccess_WhenUnconfigured()
    {
        var sender = new InMemoryNotificationSender(NotificationChannel.Email);
        var message = CreateMessage();

        var result = await sender.SendAsync(message, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(message.NotificationDeliveryId, result.NotificationDeliveryId);
        Assert.Same(message, sender.ShouldHaveSent<ReceiptModel>());
    }

    [Fact]
    public async Task SendAsync_SetSendResult_OverridesDefaultSuccess()
    {
        var sender = new InMemoryNotificationSender(NotificationChannel.Email);
        var message = CreateMessage();
        var expected = new NotificationDeliveryResult(message.NotificationDeliveryId, false, null, "provider rejected");
        sender.SetSendResult<ReceiptModel>(_ => expected);

        var result = await sender.SendAsync(message, CancellationToken.None);

        Assert.Equal(expected, result);
    }

    [Fact]
    public async Task SendAsync_NullMessage_Throws() =>
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await new InMemoryNotificationSender(NotificationChannel.Email)
                .SendAsync<ReceiptModel>(null!, CancellationToken.None));

    [Fact]
    public void ShouldHaveSent_NoMatch_Throws() =>
        Assert.Throws<InvalidOperationException>(
            () => new InMemoryNotificationSender(NotificationChannel.Email).ShouldHaveSent<ReceiptModel>());

    [Fact]
    public async Task ShouldHaveSent_WithFilter_MatchesPredicate()
    {
        var sender = new InMemoryNotificationSender(NotificationChannel.Email);
        var targetId = Guid.NewGuid();
        await sender.SendAsync(CreateMessage(), CancellationToken.None);
        await sender.SendAsync(CreateMessage(targetId), CancellationToken.None);

        var found = sender.ShouldHaveSent<ReceiptModel>(m => m.NotificationDeliveryId == targetId);

        Assert.Equal(targetId, found.NotificationDeliveryId);
    }

    [Fact]
    public void ShouldNotHaveSent_NoMatch_DoesNotThrow() =>
        new InMemoryNotificationSender(NotificationChannel.Email).ShouldNotHaveSent<ReceiptModel>();

    [Fact]
    public async Task ShouldNotHaveSent_HasMatch_Throws()
    {
        var sender = new InMemoryNotificationSender(NotificationChannel.Email);
        await sender.SendAsync(CreateMessage(), CancellationToken.None);

        Assert.Throws<InvalidOperationException>(sender.ShouldNotHaveSent<ReceiptModel>);
    }

    [Fact]
    public async Task Sent_RecordsAcrossDifferentTemplateModelTypes()
    {
        var sender = new InMemoryNotificationSender(NotificationChannel.Email);
        await sender.SendAsync(CreateMessage(), CancellationToken.None);
        var otherMessage = new NotificationMessage<string>
        {
            NotificationDeliveryId = Guid.NewGuid(),
            Channel = NotificationChannel.Email,
            Recipient = "test@example.com",
            TemplateId = "other-template",
            TemplateModel = "plain text model",
        };
        await sender.SendAsync(otherMessage, CancellationToken.None);

        Assert.Equal(2, sender.Sent.Count);
        Assert.Single(sender.SentOf<ReceiptModel>());
        Assert.Single(sender.SentOf<string>());
    }
}

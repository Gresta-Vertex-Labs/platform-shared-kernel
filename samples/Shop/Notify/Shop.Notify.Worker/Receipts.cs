using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using SharedKernel.Configuration;
using SharedKernel.Contracts.Events;
using SharedKernel.Execution.Context;
using SharedKernel.Integration.Notifications.Abstractions.Notifications;
using SharedKernel.Integration.Notifications.Abstractions.Observability;
using SharedKernel.Messaging.MassTransit.Consumers;
using SharedKernel.Validation;
using Shop.Contracts.Billing;

namespace Shop.Notify.Worker;

/// <summary>Notify's settings (<c>Notify</c>).</summary>
public sealed class NotifyOptions : ISectionBoundOptions
{
    public static string SectionName => "Notify";

    [Required]
    public SenderOptions Sender { get; set; } = new();

    /// <summary>The email provider's template for a receipt (a SendGrid dynamic template id).</summary>
    [Required]
    public string ReceiptTemplateId { get; set; } = string.Empty;

    /// <summary>The SMS provider's template for "an order was paid" (a Twilio content SID).</summary>
    [Required]
    public string MerchantTextTemplateId { get; set; } = string.Empty;

    /// <summary>Each tenant's merchant phone, E.164; a tenant without one is not texted.</summary>
    public Dictionary<string, string> MerchantPhones { get; set; } = [];

    public sealed class SenderOptions
    {
        [Required]
        [EmailAddress]
        public string Address { get; set; } = string.Empty;

        public string? DisplayName { get; set; }
    }
}

/// <summary>The receipt email's template data.</summary>
public sealed record ReceiptEmailModel(string OrderNumber, string Amount, string Currency);

/// <summary>The merchant text's template data.</summary>
public sealed record PaidOrderTextModel(string OrderNumber, string Amount);

/// <summary>Who the Shop's emails come from.</summary>
public sealed class ShopSenderIdentity(IOptions<NotifyOptions> options)
    : INotificationSenderIdentityResolver
{
    public Task<NotificationSenderIdentity> ResolveAsync(
        NotificationChannel channel,
        CancellationToken ct
    ) =>
        Task.FromResult(
            new NotificationSenderIdentity(
                options.Value.Sender.Address,
                options.Value.Sender.DisplayName
            )
        );
}

/// <summary>
/// A payment owes its customer a receipt: email the customer, text the tenant's merchant. Each notification's delivery
/// id is derived from the event and the channel, so a redelivered event reuses it: Twilio sends it as its idempotency
/// key and drops the repeat; SendGrid only records it (its delivery id is metadata, not a deduplication key), so a
/// redelivery after the email was accepted can send the receipt twice. A provider that refuses fails the message, and
/// the bus retries it.
/// </summary>
public sealed class ReceiptDueConsumer(
    [FromKeyedServices(NotificationChannel.Email)] INotificationSender email,
    [FromKeyedServices(NotificationChannel.Sms)] INotificationSender sms,
    IRequestContext caller,
    IOptions<NotifyOptions> options,
    ILogger<ReceiptDueConsumer> logger
) : ConsumerBase<EventEnvelope<ReceiptDue>>(logger)
{
    protected override async Task ConsumeAsync(
        EventEnvelope<ReceiptDue> envelope,
        CancellationToken ct
    )
    {
        var receipt = envelope.Data;
        string order = OrderNumber(receipt.OrderId);
        string amount = receipt.Amount.ToString("N2", CultureInfo.InvariantCulture);

        var emailed = await email.SendAsync(
            new NotificationMessage<ReceiptEmailModel>
            {
                NotificationDeliveryId = DeliveryId(receipt.EventId, NotificationChannel.Email),
                Channel = NotificationChannel.Email,
                Recipient = receipt.CustomerEmail,
                TemplateId = options.Value.ReceiptTemplateId,
                TemplateModel = new ReceiptEmailModel(order, amount, receipt.Currency),
            },
            ct
        );
        if (!emailed.IsSuccess)
        {
            throw new InvalidOperationException(
                $"The receipt email was not accepted: {emailed.Error}"
            );
        }

        // The merchant's phone is configuration, so it is validated here, where a bad value would otherwise go to Twilio.
        if (
            caller.TenantId is not { } tenant
            || !options.Value.MerchantPhones.TryGetValue(
                tenant.Value.ToString("D"),
                out string? phone
            )
            || PhoneNumber.Create(phone) is not { IsSuccess: true } merchantPhone
        )
        {
            return;
        }

        var texted = await sms.SendAsync(
            new NotificationMessage<PaidOrderTextModel>
            {
                NotificationDeliveryId = DeliveryId(receipt.EventId, NotificationChannel.Sms),
                Channel = NotificationChannel.Sms,
                Recipient = merchantPhone.Value.Value,
                TemplateId = options.Value.MerchantTextTemplateId,
                TemplateModel = new PaidOrderTextModel(order, $"{amount} {receipt.Currency}"),
            },
            ct
        );
        if (!texted.IsSuccess)
        {
            throw new InvalidOperationException(
                $"The merchant text was not accepted: {texted.Error}"
            );
        }
    }

    /// <summary>
    /// The order as a customer reads it: the last 8 hex digits of its id. Order ids are UUIDv7, whose leading digits are a
    /// timestamp shared by every order of the same minute; the trailing ones are random.
    /// </summary>
    public static string OrderNumber(Guid orderId) =>
        orderId.ToString("D", CultureInfo.InvariantCulture)[^8..].ToUpperInvariant();

    /// <summary>The same event and channel always give the same delivery id.</summary>
    public static Guid DeliveryId(Guid eventId, NotificationChannel channel) =>
        new(SHA256.HashData(Encoding.UTF8.GetBytes($"{eventId:D}/{channel}")).AsSpan(0, 16));
}

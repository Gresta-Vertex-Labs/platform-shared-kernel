using System.Text.Json;
using System.Text.Json.Serialization;
using SharedKernel.Contracts.Events;

namespace Shop.Contracts.Billing;

/// <summary>
/// Ordering asks Billing to take payment for an order. <see cref="PaymentToken"/> stands for a card the payment
/// provider tokenized in the customer's browser: Billing never sees a card number.
/// </summary>
public sealed record ChargeRequest(
    Guid OrderId,
    decimal Amount,
    string Currency,
    string CustomerEmail,
    string PaymentToken
);

/// <summary>A payment as Billing reports it.</summary>
public sealed record PaymentView(
    Guid PaymentId,
    Guid OrderId,
    string Status,
    decimal Amount,
    string Currency
);

/// <summary>The development payment tokens Billing's stand-in provider understands.</summary>
public static class PaymentTokens
{
    /// <summary>A card that is always captured.</summary>
    public const string Approved = "tok_visa";

    /// <summary>A card the provider always declines.</summary>
    public const string Declined = "tok_declined";
}

/// <summary>Billing's error codes that other services act on.</summary>
public static class BillingErrorCodes
{
    public const string PaymentDeclined = "billing.payment_declined";
}

/// <summary>A payment was captured. Sent to the merchant's webhook endpoint.</summary>
[IntegrationEvent("billing.payment-captured", Version = 1)]
public sealed record PaymentCaptured(
    Guid EventId,
    DateTimeOffset OccurredOn,
    Guid PaymentId,
    Guid OrderId,
    decimal Amount,
    string Currency
) : IIntegrationEvent;

[JsonSerializable(typeof(ChargeRequest))]
[JsonSerializable(typeof(PaymentView))]
[JsonSerializable(typeof(PaymentCaptured))]
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
public sealed partial class BillingJsonContext : JsonSerializerContext;

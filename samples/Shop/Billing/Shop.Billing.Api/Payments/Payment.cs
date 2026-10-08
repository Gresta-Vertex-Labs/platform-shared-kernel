using SharedKernel.DataPrivacy.Classification;
using SharedKernel.Domain.Aggregates;
using SharedKernel.Domain.Monetary;
using SharedKernel.Domain.StronglyTypedIds;
using SharedKernel.Execution.Tenancy;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

namespace Shop.Billing.Api.Payments;

public sealed record PaymentId(Guid Value) : StronglyTypedId<Guid>(Value)
{
    public static PaymentId New() => new(Guid.CreateVersion7());
}

public enum PaymentStatus
{
    /// <summary>The provider took the money.</summary>
    Captured = 0,

    /// <summary>The money went back to the customer.</summary>
    Refunded = 1,

    /// <summary>The customer's bank disputes the payment (the provider told us through its callback).</summary>
    Disputed = 2,
}

/// <summary>One payment for one order: at most one per order, so a retried charge finds the first.</summary>
public sealed class Payment : TenantedAuditableAggregateRoot<PaymentId>
{
    private Payment(
        PaymentId id,
        TenantId tenantId,
        Guid orderId,
        Money amount,
        string customerEmail,
        string providerReference,
        IClock clock
    )
        : base(id, tenantId, clock)
    {
        OrderId = orderId;
        Amount = amount;
        CustomerEmail = customerEmail;
        ProviderReference = providerReference;
        Status = PaymentStatus.Captured;
    }

    private Payment() { }

    public Guid OrderId { get; private set; }

    public Money Amount { get; private set; } = null!;

    /// <summary>Personal data: exported and erased on a data-subject request, redacted in logs.</summary>
    [EmailAddressData]
    public string? CustomerEmail { get; private set; }

    public string ProviderReference { get; private set; } = string.Empty;

    public PaymentStatus Status { get; private set; }

    /// <summary>The invoice: JSON, compressed, then signed in Key Vault (the signature covers the stored bytes).</summary>
    public byte[] Invoice { get; private set; } = [];

    public byte[] InvoiceSignature { get; private set; } = [];

    public string InvoiceSigningKeyId { get; private set; } = string.Empty;

    public static Payment Capture(
        TenantId tenantId,
        Guid orderId,
        Money amount,
        string customerEmail,
        string providerReference,
        IClock clock
    ) => new(PaymentId.New(), tenantId, orderId, amount, customerEmail, providerReference, clock);

    public void AttachInvoice(byte[] invoice, byte[] signature, string keyId)
    {
        Invoice = invoice;
        InvoiceSignature = signature;
        InvoiceSigningKeyId = keyId;
    }

    /// <summary>Refunds a captured payment; refunding again is a no-op, so a retried cancellation succeeds.</summary>
    public Result Refund() =>
        Status switch
        {
            PaymentStatus.Captured => Set(PaymentStatus.Refunded),
            PaymentStatus.Refunded => Result.Success(),
            _ => Result.Failure(
                Error.Conflict("billing.payment.disputed", "A disputed payment cannot be refunded.")
            ),
        };

    /// <summary>The provider reports a dispute; a refunded payment cannot be disputed.</summary>
    public Result Dispute() =>
        Status switch
        {
            PaymentStatus.Captured => Set(PaymentStatus.Disputed),
            PaymentStatus.Disputed => Result.Success(),
            _ => Result.Failure(
                Error.Conflict("billing.payment.refunded", "A refunded payment cannot be disputed.")
            ),
        };

    /// <summary>Erases the customer's personal data; the payment itself is kept for accounting.</summary>
    public void ErasePersonalData() => CustomerEmail = null;

    private Result Set(PaymentStatus status)
    {
        Status = status;
        return Result.Success();
    }
}

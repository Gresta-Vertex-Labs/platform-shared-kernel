using SharedKernel.Domain.Abstractions;
using SharedKernel.Domain.Aggregates;
using SharedKernel.Domain.Entities;
using SharedKernel.Domain.Events;
using SharedKernel.Domain.Monetary;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

namespace BillingApi.Domain;

public enum InvoiceStatus
{
    Draft = 0,
    Issued = 1,
    Paid = 2,
    Expired = 3,
}

/// <summary>An invoice with its lines. Soft-deletable: deleting it keeps the row (and its lines) for the books.</summary>
public sealed class Invoice : TenantedAuditableSoftDeletableAggregateRoot<InvoiceId>
{
    private readonly List<InvoiceLine> _lines = [];

    private Invoice(InvoiceId id, Guid tenantId, IClock clock) : base(id, tenantId, clock) { }

    private Invoice() { } // EF Core materialization

    public CustomerId CustomerId { get; private set; } = null!;

    public string Number { get; private set; } = string.Empty;

    public InvoiceStatus Status { get; private set; }

    public string TaxRateCode { get; private set; } = string.Empty;

    /// <summary>Net total of the lines. <see cref="Money"/> maps to two columns by convention.</summary>
    public Money Net { get; private set; } = null!;

    /// <summary>Gross total, set when the invoice is issued.</summary>
    public Money? Gross { get; private set; }

    public DateTimeOffset? IssuedOn { get; private set; }

    public DateTimeOffset? PaidOn { get; private set; }

    public IReadOnlyCollection<InvoiceLine> Lines => _lines.AsReadOnly();

    public static Result<Invoice> Draft(
        InvoiceId id,
        Guid tenantId,
        CustomerId customerId,
        string number,
        Currency currency,
        string taxRateCode,
        IReadOnlyCollection<(string Description, int Quantity, decimal UnitPrice)> lines,
        IClock clock)
    {
        if (lines.Count == 0)
            return Result<Invoice>.Failure(Error.Validation("invoice.lines.required", "An invoice needs at least one line."));

        var invoice = new Invoice(id, tenantId, clock)
        {
            CustomerId = customerId,
            Number = number,
            Status = InvoiceStatus.Draft,
            TaxRateCode = taxRateCode,
            Net = Money.Zero(currency),
        };

        foreach (var (description, quantity, unitPrice) in lines)
        {
            if (quantity <= 0)
                return Result<Invoice>.Failure(Error.Validation("invoice.line.quantity", "A line quantity must be positive."));

            var price = Money.Create(unitPrice, currency);
            if (!price.IsValid)
                return Result<Invoice>.Failure(Error.Validation(price.Errors));

            var line = new InvoiceLine(InvoiceLineId.New(), tenantId, description, quantity, price.Value);
            invoice._lines.Add(line);
            invoice.Net = invoice.Net.Add(line.UnitPrice.Multiply(quantity));
        }

        return Result<Invoice>.Success(invoice);
    }

    public Result Issue(decimal taxRate)
    {
        if (Status != InvoiceStatus.Draft)
            return Result.Failure(Error.Conflict("invoice.not_draft", $"Invoice {Number} is {Status}, only a draft can be issued."));

        Status = InvoiceStatus.Issued;
        IssuedOn = Now;
        Gross = Net.Add(Net.Multiply(taxRate));
        RaiseDomainEvent(now => new InvoiceIssued(Id, CustomerId, Gross) { OccurredOn = now });
        return Result.Success();
    }

    public Result MarkPaid(decimal amount)
    {
        if (Status != InvoiceStatus.Issued)
            return Result.Failure(Error.Conflict("invoice.not_issued", $"Invoice {Number} is {Status}, only an issued invoice can be paid."));
        if (amount != Gross!.Amount)
            return Result.Failure(Error.Validation("invoice.payment.amount", $"The payment must be exactly {Gross.Amount} {Gross.Currency}."));

        Status = InvoiceStatus.Paid;
        PaidOn = Now;
        return Result.Success();
    }
}

/// <summary>
/// A child entity. In a tenanted context every entity type is tenant data, so it implements
/// <see cref="IHasTenant"/> too (the row-level-security policy covers its table as well).
/// </summary>
public sealed class InvoiceLine : Entity<InvoiceLineId>, IHasTenant
{
    internal InvoiceLine(InvoiceLineId id, Guid tenantId, string description, int quantity, Money unitPrice) : base(id)
    {
        TenantId = tenantId;
        Description = description;
        Quantity = quantity;
        UnitPrice = unitPrice;
    }

    private InvoiceLine() { } // EF Core materialization

    public Guid TenantId { get; private set; }

    public string Description { get; private set; } = string.Empty;

    public int Quantity { get; private set; }

    public Money UnitPrice { get; private set; } = null!;
}

public sealed record InvoiceIssued(InvoiceId InvoiceId, CustomerId CustomerId, Money Gross) : DomainEvent;

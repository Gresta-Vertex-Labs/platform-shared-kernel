using SharedKernel.Execution.Tenancy;
using SharedKernel.Domain.Aggregates;
using SharedKernel.Domain.Events;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

namespace BillingApi.Domain;

/// <summary>
/// A tenant's customer. <see cref="Email"/> and <see cref="TaxNumber"/> are personal data: the persistence layer
/// encrypts them per column (see <c>CustomerConfiguration</c>) — the domain type knows nothing about that.
/// </summary>
public sealed class Customer : TenantedAuditableSoftDeletableAggregateRoot<CustomerId>
{
    private Customer(CustomerId id, TenantId tenantId, IClock clock) : base(id, tenantId, clock) { }

    private Customer() { } // EF Core materialization

    public string Name { get; private set; } = string.Empty;

    public string Email { get; private set; } = string.Empty;

    public string? TaxNumber { get; private set; }

    public int InvoiceCount { get; private set; }

    public static Result<Customer> Register(CustomerId id, TenantId tenantId, string name, string email, string? taxNumber, IClock clock)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Result<Customer>.Failure(Error.Validation("customer.name.required", "A customer needs a name."));
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@', StringComparison.Ordinal))
            return Result<Customer>.Failure(Error.Validation("customer.email.invalid", "A customer needs a valid email."));

        var customer = new Customer(id, tenantId, clock)
        {
            Name = name.Trim(),
            Email = email.Trim(),
            TaxNumber = string.IsNullOrWhiteSpace(taxNumber) ? null : taxNumber.Trim(),
        };
        customer.RaiseDomainEvent(now => new CustomerRegistered(customer.Id) { OccurredOn = now });
        return Result<Customer>.Success(customer);
    }

    public Result Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Result.Failure(Error.Validation("customer.name.required", "A customer needs a name."));

        Name = name.Trim();
        return Result.Success();
    }

    /// <summary>Called by the <see cref="InvoiceIssued"/> domain-event handler, inside the same save.</summary>
    public void RecordInvoiceIssued() => InvoiceCount++;
}

public sealed record CustomerRegistered(CustomerId CustomerId) : DomainEvent;

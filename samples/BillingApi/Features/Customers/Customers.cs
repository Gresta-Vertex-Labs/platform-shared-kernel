using BillingApi.Domain;
using SharedKernel.Primitives.Errors;

namespace BillingApi.Features.Customers;

public sealed record CustomerView(Guid Id, string Name, string Email, string? TaxNumber, int InvoiceCount, bool IsDeleted);

/// <summary>Reads an encrypted column by value — implemented over the blind index in the infrastructure layer.</summary>
public interface ICustomerDirectory
{
    Task<Customer?> FindByEmailAsync(string email, CancellationToken cancellationToken);
}

public static class CustomerErrors
{
    public static Error NotFound(CustomerId id) => Error.NotFound("customer.not_found", $"Customer {id.Value} was not found.");
}

public static class CustomerMapping
{
    public static CustomerView ToView(this Customer customer) =>
        new(customer.Id.Value, customer.Name, customer.Email, customer.TaxNumber, customer.InvoiceCount, customer.IsDeleted);
}

using BillingApi.Domain;
using BillingApi.Features.Customers;
using SharedKernel.Application;
using SharedKernel.Application.Context;
using SharedKernel.Domain.Monetary;
using SharedKernel.Domain.Specifications;
using SharedKernel.Persistence.Abstractions.Repositories;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

namespace BillingApi.Features.Invoices;

[RequirePermission(Permissions.Write)]
public sealed record DraftInvoice(InvoiceId Id, CustomerId CustomerId, string Currency, string TaxRateCode, IReadOnlyList<InvoiceLineInput> Lines)
    : ICommand<InvoiceId>, IAuditableRequest<Result<InvoiceId>>
{
    public string Action => "invoice.drafted";
    public string ResourceType => nameof(Invoice);
    public string ResourceId => Id.Value.ToString();
    public string? BeforeSnapshot => null;
    public string? GetAfterSnapshot(Result<InvoiceId> response) => null;
}

public sealed class DraftInvoiceHandler(
    IRepository<Invoice, InvoiceId> invoices,
    IReadRepository<Customer, CustomerId> customers,
    IReadRepository<TaxRate, string> taxRates,
    IRequestContext caller,
    IClock clock)
    : ICommandHandler<DraftInvoice, InvoiceId>
{
    public async Task<Result<InvoiceId>> Handle(DraftInvoice command, CancellationToken cancellationToken)
    {
        // Another tenant's customer is invisible here (tenant filter + row-level security): it is simply "not found".
        if (!await customers.ExistsAsync(command.CustomerId, cancellationToken))
            return Result<InvoiceId>.Failure(CustomerErrors.NotFound(command.CustomerId));

        // Tenant-shared reference data is visible to every tenant.
        if (!await taxRates.ExistsAsync(command.TaxRateCode, cancellationToken))
            return Result<InvoiceId>.Failure(Error.Validation("invoice.tax_rate.unknown", $"Unknown tax rate '{command.TaxRateCode}'."));

        var currency = Currency.Create(command.Currency);
        if (!currency.IsValid)
            return Result<InvoiceId>.Failure(Error.Validation(currency.Errors));

        // The number is per tenant; the unique (tenant_id, number) index turns a race into a 409.
        var count = await invoices.CountAsync(Spec.For<Invoice>().IncludeDeleted(), cancellationToken);
        var number = $"INV-{count + 1:D5}";

        var invoice = Invoice.Draft(
            command.Id, caller.TenantId!.Value, command.CustomerId, number, currency.Value, command.TaxRateCode,
            [.. command.Lines.Select(l => (l.Description, l.Quantity, l.UnitPrice))], clock);
        if (invoice.IsFailure)
            return Result<InvoiceId>.Failure(invoice.Error);

        await invoices.AddAsync(invoice.Value, cancellationToken);
        return Result<InvoiceId>.Success(invoice.Value.Id);
    }
}

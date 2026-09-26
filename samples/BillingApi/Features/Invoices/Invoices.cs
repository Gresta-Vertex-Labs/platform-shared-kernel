using BillingApi.Domain;
using SharedKernel.Primitives.Errors;

namespace BillingApi.Features.Invoices;

public sealed record InvoiceLineInput(string Description, int Quantity, decimal UnitPrice);

public sealed record InvoiceView(
    Guid Id,
    string Number,
    Guid CustomerId,
    string Status,
    decimal Net,
    decimal? Gross,
    string Currency,
    DateTimeOffset CreatedOn,
    IReadOnlyList<InvoiceLineInput> Lines);

public static class InvoiceErrors
{
    public static Error NotFound(InvoiceId id) => Error.NotFound("invoice.not_found", $"Invoice {id.Value} was not found.");
}

public static class InvoiceMapping
{
    public static InvoiceView ToView(this Invoice invoice) => new(
        invoice.Id.Value,
        invoice.Number,
        invoice.CustomerId.Value,
        invoice.Status.ToString(),
        invoice.Net.Amount,
        invoice.Gross?.Amount,
        invoice.Net.Currency.Code,
        invoice.CreatedOn,
        [.. invoice.Lines.Select(l => new InvoiceLineInput(l.Description, l.Quantity, l.UnitPrice.Amount))]);
}

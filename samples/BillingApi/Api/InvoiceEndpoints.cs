using BillingApi.Domain;
using BillingApi.Features.Invoices;
using SharedKernel.Application.Messaging;
using SharedKernel.Presentation.WebApi;

namespace BillingApi.Api;

public sealed record DraftInvoiceRequest(Guid CustomerId, string Currency, string TaxRate, IReadOnlyList<InvoiceLineInput> Lines);

public sealed record PaymentRequest(decimal Amount, string Reference);

public sealed record ExpireDraftsRequest(DateTimeOffset DraftedBefore);

/// <summary>The body of <c>POST /invoices/expire-drafts</c>.</summary>
public sealed record DraftsExpired(int Expired);

/// <summary><c>/invoices</c>: drafting, issuing and paying, and three ways to read.</summary>
public sealed class InvoiceEndpoints : IEndpointModule
{
    public static void Map(IEndpointRouteBuilder app)
    {
        var invoices = app.MapGroup("/invoices");

        invoices.MapPost("/", (DraftInvoiceRequest body, ISender sender, CancellationToken ct) =>
            sender.Send(new DraftInvoice(InvoiceId.New(), new CustomerId(body.CustomerId), body.Currency, body.TaxRate, body.Lines), ct)
                .ToCreated(id => $"/invoices/{id.Value}", id => new ResourceCreated(id.Value)));

        invoices.MapGet("/{id:guid}", (Guid id, ISender sender, CancellationToken ct) =>
            sender.Send(new GetInvoice(new InvoiceId(id)), ct).ToOk());

        // Paging and CursorPaging bind the query into validated requests; invalid input is 400 before the handler.
        invoices.MapGet("/", (Paging paging, string? status, ISender sender, CancellationToken ct) =>
            sender.Send(new ListInvoices(paging.Request, ParseStatus(status)), ct).ToOk());

        invoices.MapGet("/browse", (CursorPaging paging, ISender sender, CancellationToken ct) =>
            sender.Send(new BrowseInvoices(paging.Request), ct).ToOk());

        invoices.MapPost("/{id:guid}/issue", (Guid id, ISender sender, CancellationToken ct) =>
            sender.Send(new IssueInvoice(new InvoiceId(id)), ct).ToNoContent());

        invoices.MapPost("/{id:guid}/payments", (Guid id, PaymentRequest body, ISender sender, CancellationToken ct) =>
            sender.Send(new PayInvoice(new InvoiceId(id), body.Amount, body.Reference), ct).ToNoContent());

        invoices.MapPost("/expire-drafts", (ExpireDraftsRequest body, ISender sender, CancellationToken ct) =>
            sender.Send(new ExpireStaleDrafts(body.DraftedBefore), ct).ToOk(count => new DraftsExpired(count)));
    }

    /// <summary>An unknown status filters nothing, as it always has.</summary>
    private static InvoiceStatus? ParseStatus(string? status) =>
        Enum.TryParse<InvoiceStatus>(status, ignoreCase: true, out var parsed) ? parsed : null;
}

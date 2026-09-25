using BillingApi.Domain;
using SharedKernel.Application;
using SharedKernel.Contracts.Pagination;
using SharedKernel.Domain.Specifications;
using SharedKernel.Persistence.Abstractions.Repositories;
using SharedKernel.Primitives.Results;

namespace BillingApi.Features.Invoices;

/// <summary>Keyset (cursor) pages, newest first.</summary>
[RequirePermission(Permissions.Read)]
public sealed record BrowseInvoices(CursorPageRequest Page) : IQuery<CursorPagedList<InvoiceView>>;

public sealed class BrowseInvoicesHandler(IReadRepository<Invoice, InvoiceId> invoices) : IQueryHandler<BrowseInvoices, CursorPagedList<InvoiceView>>
{
    public async Task<Result<CursorPagedList<InvoiceView>>> Handle(BrowseInvoices query, CancellationToken cancellationToken)
    {
        // Keyset: the spec filters, the key selector orders; the id is the tiebreaker. Never OFFSET.
        var page = await invoices.ListKeysetAsync(Spec.For<Invoice>(), query.Page, i => i.CreatedOn, descending: true, cancellationToken);
        return Result<CursorPagedList<InvoiceView>>.Success(page.Map(i => i.ToView()));
    }
}

using BillingApi.Domain;
using SharedKernel.Application;
using SharedKernel.Contracts.Pagination;
using SharedKernel.Domain.Specifications;
using SharedKernel.Persistence.Abstractions.Repositories;
using SharedKernel.Primitives.Results;

namespace BillingApi.Features.Invoices;

/// <summary>Offset pages, newest first.</summary>
[RequirePermission(Permissions.Read)]
public sealed record ListInvoices(PageRequest Page, InvoiceStatus? Status) : IQuery<PagedList<InvoiceView>>;

public sealed class ListInvoicesHandler(IReadRepository<Invoice, InvoiceId> invoices) : IQueryHandler<ListInvoices, PagedList<InvoiceView>>
{
    public async Task<Result<PagedList<InvoiceView>>> Handle(ListInvoices query, CancellationToken cancellationToken)
    {
        var spec = Spec.For<Invoice>().OrderByDescending(i => i.CreatedOn).ThenByDescending(i => i.Number);
        if (query.Status is { } status)
            spec = spec.Where(i => i.Status == status);

        var page = await invoices.ListPagedAsync(spec, query.Page, cancellationToken);
        return Result<PagedList<InvoiceView>>.Success(page.Map(i => i.ToView()));
    }
}

using BillingApi.Domain;
using SharedKernel.Application.Authorization;
using SharedKernel.Application.Messaging;
using SharedKernel.Persistence.Abstractions.Repositories;
using SharedKernel.Primitives.Results;

namespace BillingApi.Features.Invoices;

[RequirePermission(Permissions.Read)]
public sealed record GetInvoice(InvoiceId Id) : IQuery<InvoiceView>;

public sealed class GetInvoiceHandler(IReadRepository<Invoice, InvoiceId> invoices) : IQueryHandler<GetInvoice, InvoiceView>
{
    public async Task<Result<InvoiceView>> Handle(GetInvoice query, CancellationToken cancellationToken)
    {
        var invoice = await invoices.GetByIdAsync(query.Id, cancellationToken);
        return invoice is null
            ? Result<InvoiceView>.Failure(InvoiceErrors.NotFound(query.Id))
            : Result<InvoiceView>.Success(invoice.ToView());
    }
}

using BillingApi.Domain;
using MediatR;
using SharedKernel.Application;
using SharedKernel.Persistence.Abstractions.Repositories;
using SharedKernel.Primitives.Results;

namespace BillingApi.Features.Invoices;

/// <summary>Issues a draft; the aggregate raises <see cref="InvoiceIssued"/>, handled inside the same save.</summary>
[RequirePermission(Permissions.Write)]
public sealed record IssueInvoice(InvoiceId Id) : ICommand, IAuditableRequest<Result>
{
    public string Action => "invoice.issued";
    public string ResourceType => nameof(Invoice);
    public string ResourceId => Id.Value.ToString();
    public string? BeforeSnapshot => null;
    public string? GetAfterSnapshot(Result response) => null;
}

public sealed class IssueInvoiceHandler(IRepository<Invoice, InvoiceId> invoices, IReadRepository<TaxRate, string> taxRates)
    : ICommandHandler<IssueInvoice>
{
    public async Task<Result> Handle(IssueInvoice command, CancellationToken cancellationToken)
    {
        var invoice = await invoices.GetByIdAsync(command.Id, cancellationToken);
        if (invoice is null)
            return Result.Failure(InvoiceErrors.NotFound(command.Id));

        var rate = await taxRates.GetByIdAsync(invoice.TaxRateCode, cancellationToken);
        return invoice.Issue(rate!.Rate);
    }
}

/// <summary>
/// Domain events are dispatched by the context before the physical save, so this handler's change to the customer
/// is written in the same transaction as the invoice.
/// </summary>
public sealed class InvoiceIssuedHandler(IRepository<Customer, CustomerId> customers)
    : INotificationHandler<DomainEventNotification<InvoiceIssued>>
{
    public async Task Handle(DomainEventNotification<InvoiceIssued> notification, CancellationToken cancellationToken)
    {
        var customer = await customers.GetByIdAsync(notification.DomainEvent.CustomerId, cancellationToken);
        customer?.RecordInvoiceIssued();
    }
}

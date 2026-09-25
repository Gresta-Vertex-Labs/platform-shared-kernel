using BillingApi.Domain;
using Dapper;
using MediatR;
using SharedKernel.Application.Behaviors.Auditing;
using SharedKernel.Application.Behaviors.Authorization;
using SharedKernel.Execution.Context;
using SharedKernel.Application.DomainEvents;
using SharedKernel.Application.Messaging;
using SharedKernel.Contracts.Pagination;
using SharedKernel.Domain.Monetary;
using SharedKernel.Domain.Specifications;
using SharedKernel.Persistence.Abstractions.Repositories;
using SharedKernel.Persistence.Dapper.Sessions;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

namespace BillingApi.Application;

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

// ---------------------------------------------------------------------------------------------------------------
// Draft
// ---------------------------------------------------------------------------------------------------------------

public sealed record DraftInvoice(InvoiceId Id, CustomerId CustomerId, string Currency, string TaxRateCode, IReadOnlyList<InvoiceLineInput> Lines)
    : ICommand<InvoiceId>, IAuthorizeRequest, IAuditableRequest<Result<InvoiceId>>
{
    public IReadOnlyCollection<string> RequiredPermissions => [Permissions.Write];
    public PermissionMatch PermissionMatch => PermissionMatch.All;

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

// ---------------------------------------------------------------------------------------------------------------
// Issue — raises InvoiceIssued, handled inside the same save
// ---------------------------------------------------------------------------------------------------------------

public sealed record IssueInvoice(InvoiceId Id) : ICommand, IAuthorizeRequest, IAuditableRequest<Result>
{
    public IReadOnlyCollection<string> RequiredPermissions => [Permissions.Write];
    public PermissionMatch PermissionMatch => PermissionMatch.All;

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

// ---------------------------------------------------------------------------------------------------------------
// Pay — a Dapper write and an EF Core write in ONE transaction
// ---------------------------------------------------------------------------------------------------------------

public sealed record PayInvoice(InvoiceId Id, decimal Amount, string Reference) : ICommand, IAuthorizeRequest, IAuditableRequest<Result>
{
    public IReadOnlyCollection<string> RequiredPermissions => [Permissions.Write];
    public PermissionMatch PermissionMatch => PermissionMatch.All;

    public string Action => "invoice.paid";
    public string ResourceType => nameof(Invoice);
    public string ResourceId => Id.Value.ToString();
    public string? BeforeSnapshot => null;
    public string? GetAfterSnapshot(Result response) => response.IsSuccess ? $$"""{"amount":{{Amount}}}""" : null;
}

public sealed class PayInvoiceHandler(IRepository<Invoice, InvoiceId> invoices, IDbSessionFactory sessions, IClock clock)
    : ICommandHandler<PayInvoice>
{
    public async Task<Result> Handle(PayInvoice command, CancellationToken cancellationToken)
    {
        var invoice = await invoices.GetByIdAsync(command.Id, cancellationToken);
        if (invoice is null)
            return Result.Failure(InvoiceErrors.NotFound(command.Id));

        // The session joins TransactionBehavior's transaction and binds the caller's tenant for row-level security.
        // The payment row is written first on purpose: when MarkPaid fails below, the failed Result rolls back BOTH
        // writes — the payments table never holds a payment for an unpaid invoice.
        await using (var session = await sessions.OpenAsync(cancellationToken))
        {
            await session.Connection.ExecuteAsync(session.Command(
                """
                INSERT INTO payments (id, tenant_id, invoice_id, amount, reference, received_on)
                VALUES (@id, @tenantId, @invoiceId, @amount, @reference, @receivedOn)
                """,
                new
                {
                    id = Guid.CreateVersion7(),
                    tenantId = session.RequireTenantId(),
                    invoiceId = command.Id.Value,
                    amount = command.Amount,
                    reference = command.Reference,
                    receivedOn = clock.UtcNow,
                },
                cancellationToken));
        }

        return invoice.MarkPaid(command.Amount);
    }
}

// ---------------------------------------------------------------------------------------------------------------
// Bulk: expire stale drafts in one UPDATE, without loading them
// ---------------------------------------------------------------------------------------------------------------

public sealed record ExpireStaleDrafts(DateTimeOffset DraftedBefore) : ICommand<int>, IAuthorizeRequest
{
    public IReadOnlyCollection<string> RequiredPermissions => [Permissions.Write];
    public PermissionMatch PermissionMatch => PermissionMatch.All;
}

public sealed class ExpireStaleDraftsHandler(IBulkMutationRepository<Invoice, InvoiceId> bulk) : ICommandHandler<ExpireStaleDrafts, int>
{
    public async Task<Result<int>> Handle(ExpireStaleDrafts command, CancellationToken cancellationToken)
    {
        var stale = Spec.For<Invoice>().Where(i => i.Status == InvoiceStatus.Draft && i.CreatedOn < command.DraftedBefore);
        var expired = await bulk.ExecuteUpdateAsync(stale, s => s.SetProperty(i => i.Status, InvoiceStatus.Expired), cancellationToken);
        return Result<int>.Success(expired);
    }
}

// ---------------------------------------------------------------------------------------------------------------
// Queries: by id, offset pages, keyset (cursor) pages
// ---------------------------------------------------------------------------------------------------------------

public sealed record GetInvoice(InvoiceId Id) : IQuery<InvoiceView>, IAuthorizeRequest
{
    public IReadOnlyCollection<string> RequiredPermissions => [Permissions.Read];
    public PermissionMatch PermissionMatch => PermissionMatch.All;
}

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

public sealed record ListInvoices(PageRequest Page, InvoiceStatus? Status) : IQuery<PagedList<InvoiceView>>, IAuthorizeRequest
{
    public IReadOnlyCollection<string> RequiredPermissions => [Permissions.Read];
    public PermissionMatch PermissionMatch => PermissionMatch.All;
}

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

public sealed record BrowseInvoices(CursorPageRequest Page) : IQuery<CursorPagedList<InvoiceView>>, IAuthorizeRequest
{
    public IReadOnlyCollection<string> RequiredPermissions => [Permissions.Read];
    public PermissionMatch PermissionMatch => PermissionMatch.All;
}

public sealed class BrowseInvoicesHandler(IReadRepository<Invoice, InvoiceId> invoices) : IQueryHandler<BrowseInvoices, CursorPagedList<InvoiceView>>
{
    public async Task<Result<CursorPagedList<InvoiceView>>> Handle(BrowseInvoices query, CancellationToken cancellationToken)
    {
        // Keyset: the spec filters, the key selector orders; the id is the tiebreaker. Never OFFSET.
        var page = await invoices.ListKeysetAsync(Spec.For<Invoice>(), query.Page, i => i.CreatedOn, descending: true, cancellationToken);
        return Result<CursorPagedList<InvoiceView>>.Success(page.Map(i => i.ToView()));
    }
}

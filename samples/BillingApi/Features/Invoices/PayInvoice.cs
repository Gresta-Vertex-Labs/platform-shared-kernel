using BillingApi.Domain;
using Dapper;
using SharedKernel.Application.Auditing;
using SharedKernel.Application.Authorization;
using SharedKernel.Application.Messaging;
using SharedKernel.Persistence.Abstractions.Repositories;
using SharedKernel.Persistence.Dapper.Sessions;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Primitives.Results;

namespace BillingApi.Features.Invoices;

/// <summary>A Dapper write and an EF Core write in ONE transaction.</summary>
[RequirePermission(Permissions.Write)]
public sealed record PayInvoice(InvoiceId Id, decimal Amount, string Reference) : ICommand, IAuditableRequest<Result>
{
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

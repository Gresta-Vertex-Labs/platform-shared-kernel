using BillingApi.Domain;
using SharedKernel.Application;
using SharedKernel.Domain.Specifications;
using SharedKernel.Persistence.Abstractions.Repositories;
using SharedKernel.Primitives.Results;

namespace BillingApi.Features.Invoices;

/// <summary>Expires stale drafts in one UPDATE, without loading them.</summary>
[RequirePermission(Permissions.Write)]
public sealed record ExpireStaleDrafts(DateTimeOffset DraftedBefore) : ICommand<int>;

public sealed class ExpireStaleDraftsHandler(IBulkMutationRepository<Invoice, InvoiceId> bulk) : ICommandHandler<ExpireStaleDrafts, int>
{
    public async Task<Result<int>> Handle(ExpireStaleDrafts command, CancellationToken cancellationToken)
    {
        var stale = Spec.For<Invoice>().Where(i => i.Status == InvoiceStatus.Draft && i.CreatedOn < command.DraftedBefore);
        var expired = await bulk.ExecuteUpdateAsync(stale, s => s.SetProperty(i => i.Status, InvoiceStatus.Expired), cancellationToken);
        return Result<int>.Success(expired);
    }
}

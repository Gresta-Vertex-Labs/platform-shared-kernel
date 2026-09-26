using SharedKernel.Application.Authorization;
using SharedKernel.Application.Messaging;
using SharedKernel.Persistence.EfCore.Auditing;
using SharedKernel.Primitives.Results;

namespace BillingApi.Features.Audit;

public sealed record AuditLine(Guid Id, string Action, string Outcome, string ActorId, string ActorKind, DateTimeOffset OccurredOn);

[RequirePermission(Permissions.Read)]
public sealed record GetAuditHistory(string ResourceType, string ResourceId) : IQuery<IReadOnlyList<AuditLine>>;

public sealed class GetAuditHistoryHandler(IAuditQueryService audit) : IQueryHandler<GetAuditHistory, IReadOnlyList<AuditLine>>
{
    public async Task<Result<IReadOnlyList<AuditLine>>> Handle(GetAuditHistory query, CancellationToken cancellationToken)
    {
        var page = await audit.QueryAsync(new AuditRecordQuery { ResourceType = query.ResourceType, ResourceId = query.ResourceId }, cancellationToken);
        return Result<IReadOnlyList<AuditLine>>.Success(
            [.. page.Items.Select(r => new AuditLine(r.Id, r.Action, r.Outcome.ToString(), r.ActorId, r.ActorKind.ToString(), r.OccurredOn))]);
    }
}

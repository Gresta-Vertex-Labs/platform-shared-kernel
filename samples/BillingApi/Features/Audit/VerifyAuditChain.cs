using SharedKernel.Application.Authorization;
using SharedKernel.Application.Messaging;
using SharedKernel.Persistence.EfCore.Auditing;
using SharedKernel.Primitives.Results;

namespace BillingApi.Features.Audit;

public sealed record AuditChainStatus(string ResourceType, string Status, long RecordsChecked, long? HeadSequence, string? FailureKind, string? Reason);

[RequirePermission(Permissions.Read)]
public sealed record VerifyAuditChain(string ResourceType) : IQuery<AuditChainStatus>;

public sealed class VerifyAuditChainHandler(IAuditQueryService audit) : IQueryHandler<VerifyAuditChain, AuditChainStatus>
{
    public async Task<Result<AuditChainStatus>> Handle(VerifyAuditChain query, CancellationToken cancellationToken)
    {
        var result = await audit.VerifyChainAsync(query.ResourceType, cancellationToken: cancellationToken);
        return Result<AuditChainStatus>.Success(new AuditChainStatus(
            query.ResourceType,
            result.Status.ToString(),
            result.RecordsChecked,
            result.HeadSequence,
            result.IsIntact ? null : result.FailureKind.ToString(),
            result.Reason));
    }
}

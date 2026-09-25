using SharedKernel.Execution.Auditing;

namespace SharedKernel.Application.Behaviors.Auditing;

/// <summary>Builds the <see cref="AuditEntry"/> both auditing behaviors record.</summary>
internal static class AuditEntries
{
    internal static AuditEntry Succeeded<TResponse>(IAuditableRequest<TResponse> request, TResponse response) => new()
    {
        Action = request.Action,
        ResourceType = request.ResourceType,
        ResourceId = request.ResourceId,
        BeforeSnapshot = request.BeforeSnapshot,
        AfterSnapshot = request.GetAfterSnapshot(response),
        Outcome = AuditOutcome.Succeeded,
    };

    internal static AuditEntry Failed<TResponse>(IAuditableRequest<TResponse> request, string errorCode) => new()
    {
        Action = request.Action,
        ResourceType = request.ResourceType,
        ResourceId = request.ResourceId,
        BeforeSnapshot = request.BeforeSnapshot,
        Outcome = AuditOutcome.Failed,
        ErrorCode = errorCode,
    };
}

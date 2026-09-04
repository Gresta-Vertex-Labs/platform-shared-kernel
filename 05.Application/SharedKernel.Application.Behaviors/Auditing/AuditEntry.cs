namespace SharedKernel.Application.Behaviors.Auditing;

/// <summary>
/// A local-seam audit entry passed to <see cref="IAuditTrailWriter.RecordAsync"/>.
/// </summary>
/// <param name="Action">The audited action identifier.</param>
/// <param name="ResourceType">The type of resource affected by the action.</param>
/// <param name="ResourceId">The identifier of the specific resource instance affected.</param>
/// <param name="BeforeSnapshot">The caller-pre-serialized before-state, or <see langword="null"/>.</param>
/// <param name="AfterSnapshot">The caller-pre-serialized after-state, or <see langword="null"/>.</param>
/// <param name="ApprovalId">
/// The linked dual-control approval key when the audited command also implements
/// <see cref="DualApproval.IRequiresDualApproval"/>; otherwise <see langword="null"/>. Populated by
/// <see cref="AuditingBehavior{TRequest,TResponse}"/> itself via an <see langword="is"/>-pattern
/// check — never supplied by <see cref="IAuditableRequest{TResponse}"/>.
/// </param>
/// <remarks>
/// Deliberately omits <c>Id</c>/<c>ActorId</c>/<c>TenantId</c>/<c>OccurredOn</c>/<c>CorrelationId</c>/
/// <c>RecordHash</c>/<c>PreviousRecordHash</c> — every one of those is resolved by the REAL
/// <c>06.Persistence</c> writer implementation, never by this package or by the caller. This is a
/// deliberately smaller, local-seam counterpart to <c>SharedKernel.Persistence.Abstractions</c>'s
/// richer <c>AuditEntry</c> of the same name — see <see cref="IAuditTrailWriter"/>'s remarks for the
/// full same-name-different-namespace bridging rationale.
/// </remarks>
public sealed record AuditEntry(
    string Action,
    string ResourceType,
    string ResourceId,
    string? BeforeSnapshot,
    string? AfterSnapshot,
    string? ApprovalId);

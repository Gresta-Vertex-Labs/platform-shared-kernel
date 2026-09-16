namespace SharedKernel.Application.Behaviors.Auditing;

/// <summary>
/// A local-seam audit entry passed to <see cref="IAuditTrailWriter.RecordAsync"/>.
/// </summary>
/// <param name="Action">The audited action identifier.</param>
/// <param name="ResourceType">The type of resource affected by the action.</param>
/// <param name="ResourceId">The identifier of the specific resource instance affected.</param>
/// <param name="BeforeSnapshot">The caller-pre-serialized before-state, or <see langword="null"/>.</param>
/// <param name="AfterSnapshot">
/// The caller-pre-serialized after-state, or <see langword="null"/>. Only ever computed on a
/// successful outcome — always <see langword="null"/> when <paramref name="Succeeded"/> is
/// <see langword="false"/>, since a rejected command produced no new state to snapshot.
/// </param>
/// <param name="Succeeded">
/// <see langword="true"/> when the audited command completed successfully; <see langword="false"/>
/// for both a <c>Result.Failure</c> outcome and a thrown exception — a faulted attempt is recorded
/// too, not skipped.
/// </param>
/// <param name="ErrorCode">
/// <see langword="null"/> on success. On a <c>Result.Failure</c>, the failed command's
/// <c>Error.Code</c>. On a thrown exception, the exception type's full name (e.g.
/// <c>"System.InvalidOperationException"</c>) — there is no <c>Error.Code</c> to record, but the
/// fact that a fault occurred, and which kind, is still audit-relevant.
/// </param>
/// <remarks>
/// Deliberately omits <c>Id</c>/<c>ActorId</c>/<c>TenantId</c>/<c>OccurredOn</c>/<c>CorrelationId</c>/
/// <c>RecordHash</c>/<c>PreviousRecordHash</c> — every one of those is resolved by the real
/// <c>06.Persistence</c> writer implementation, never by this package or the caller. This is a
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
    bool Succeeded,
    string? ErrorCode);

using SharedKernel.Persistence.Abstractions.Context;

namespace SharedKernel.Persistence.Abstractions.Auditing;

/// <summary>
/// Writes append-only <see cref="AuditRecord"/> entries to the audit trail.
/// </summary>
/// <remarks>
/// <para>
/// This interface has exactly ONE member, and it is not
/// update or delete — that is the structural (not conventional) immutability guarantee this capability
/// provides. There is no way to modify or remove an existing <see cref="AuditRecord"/> through this
/// contract, full stop.
/// </para>
/// <para>
/// Actor and tenant identity are resolved internally by the implementation via
/// <see cref="ICurrentActorContext"/>/<see cref="ICurrentTenantContext"/> — never accepted as
/// parameters here, so a caller cannot forge who performed an action.
/// <see cref="AuditRecord.OccurredOn"/> is resolved from <c>IClock</c> and
/// <see cref="AuditRecord.CorrelationId"/> from ambient <see cref="System.Diagnostics.Activity"/>
/// baggage. Hash chaining is scoped per <c>(TenantId, ResourceType)</c> chain — never globally — so
/// unrelated resources' audit streams never serialize against each other; see <see cref="AuditRecord"/>'s
/// remarks for the full chain-partition rationale.
/// </para>
/// <para>
/// <strong>Transaction semantics — the exact rule:</strong>
/// </para>
/// <list type="bullet">
/// <item>
/// <description>
/// <c>entry.Outcome == AuditOutcome.Succeeded</c>: the new record is written on the SAME database
/// connection and transaction as the caller's own business write — REQUIRED, never merely preferred
/// (the EF Core implementation resolves this via <c>IAmbientDbTransaction</c>). It commits or rolls
/// back ATOMICALLY together with that business write — if something else in the same transaction
/// later fails and the whole transaction rolls back, this "succeeded" record vanishes with it, which
/// is correct: the business action it describes did not, in the end, actually happen either. Calling
/// this with <c>Outcome.Succeeded</c> while NO ambient transaction is active throws
/// <see cref="InvalidOperationException"/> rather than silently falling back to a standalone,
/// independently-committed write — an unconditional "succeeded" attestation with no transactional tie
/// to the business write it describes would be worse than no attestation at all: it could commit
/// before, and regardless of, a business write that never actually happens.
/// </description>
/// </item>
/// <item>
/// <description>
/// <c>entry.Outcome == AuditOutcome.Failed</c>: the new record is ALWAYS written on its OWN
/// connection and its OWN transaction, committed immediately by this call — regardless of whether
/// an ambient business transaction exists or what happens to it afterward. A failed/rejected
/// attempt is itself often the compliance-relevant event; recording it must not be contingent on,
/// or reversible by, the business transaction it describes rolling back. This is why a failed
/// command's business-data changes are never committed (they roll back with the transaction that
/// wraps them), while the fact that the attempt happened, and why it failed, always survives.
/// </description>
/// </item>
/// </list>
/// <para>
/// This capability is never fed automatically off the existing <c>AuditInterceptor</c> — recording an
/// audit entry is always an explicit act by the caller (typically via <c>05.Application</c>'s opt-in
/// <c>IAuditableRequest</c>/<c>AuditingBehavior</c>, bridged to this contract at the composition root).
/// </para>
/// </remarks>
public interface IAuditTrailWriter
{
    /// <summary>
    /// Appends a new <see cref="AuditRecord"/> to the audit trail for <paramref name="entry"/>.
    /// </summary>
    /// <param name="entry">The caller-supplied audit entry to record.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The fully-resolved <see cref="AuditRecord"/> that was written.</returns>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="entry"/>.<see cref="AuditEntry.Outcome"/> is <see cref="AuditOutcome.Succeeded"/>
    /// but no ambient database transaction is active — see type-level remarks.
    /// </exception>
    /// <remarks>
    /// <para>
    /// When <paramref name="entry"/>.<see cref="AuditEntry.IdempotencyKey"/> is set and a record with
    /// the same key already exists in the target chain, this call is retry-safe: it returns the
    /// EXISTING record rather than appending a duplicate — PROVIDED <paramref name="entry"/> describes
    /// the SAME logical event (<see cref="AuditEntry.Action"/>, <see cref="AuditEntry.ResourceType"/>,
    /// <see cref="AuditEntry.ResourceId"/> and <see cref="AuditEntry.Outcome"/> all match the existing
    /// record). Reusing an idempotency key for a genuinely DIFFERENT event throws
    /// <see cref="InvalidOperationException"/> rather than silently discarding it — an idempotency key
    /// must uniquely identify one logical audit event.
    /// </para>
    /// </remarks>
    Task<AuditRecord> RecordAsync(AuditEntry entry, CancellationToken cancellationToken = default);
}

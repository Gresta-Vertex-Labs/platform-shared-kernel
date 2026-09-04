namespace SharedKernel.Persistence.Abstractions.Auditing;

/// <summary>
/// Writes append-only <see cref="AuditRecord"/> entries to the audit trail.
/// </summary>
/// <remarks>
/// <para>
/// WO-071/P-456/D-115. This interface has exactly ONE member, and it is not update or delete — that
/// is the structural (not conventional) immutability guarantee this capability provides. There is no
/// way to modify or remove an existing <see cref="AuditRecord"/> through this contract, full stop.
/// </para>
/// <para>
/// Actor and tenant identity are resolved internally by the implementation via
/// <see cref="IAuditActorContext"/> — never accepted as parameters here, so a caller cannot forge
/// who performed an action. <see cref="AuditRecord.OccurredOn"/> is resolved from <c>IClock</c>.
/// Hash chaining (<see cref="AuditRecord.RecordHash"/>/<see cref="AuditRecord.PreviousRecordHash"/>)
/// is scoped per <c>(TenantId, ResourceType)</c> partition — never globally — so unrelated resources'
/// audit streams never serialize against each other.
/// </para>
/// <para>
/// This capability is never fed automatically off the existing <c>AuditInterceptor</c> — recording an
/// audit entry is always an explicit act by the caller (typically via <c>05.Application</c>'s opt-in
/// <c>IAuditableRequest</c>/<c>AuditingBehavior</c>).
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
    Task<AuditRecord> RecordAsync(AuditEntry entry, CancellationToken cancellationToken = default);
}

namespace SharedKernel.Persistence.Abstractions.Auditing;

/// <summary>
/// The outcome of the action an <see cref="AuditEntry"/>/<see cref="AuditRecord"/> describes.
/// </summary>
public enum AuditOutcome
{
    /// <summary>The audited action completed successfully.</summary>
    Succeeded = 0,

    /// <summary>
    /// The audited action did not complete — a rejected <c>Result.Failure</c> or a thrown exception
    /// alike. A rejected or faulted attempt is itself often the compliance-relevant event, so it is
    /// recorded, not skipped.
    /// </summary>
    Failed = 1,
}

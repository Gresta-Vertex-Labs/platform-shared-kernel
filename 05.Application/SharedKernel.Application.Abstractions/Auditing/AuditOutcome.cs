namespace SharedKernel.Application.Auditing;

/// <summary>The outcome of the action an <see cref="AuditEntry"/> describes.</summary>
public enum AuditOutcome
{
    /// <summary>The audited action completed and its changes committed.</summary>
    Succeeded = 0,

    /// <summary>
    /// The audited action did not complete — a failed <c>Result</c>, a thrown exception, or a commit
    /// that failed. A rejected or faulted attempt is often the compliance-relevant event, so it is
    /// recorded, not skipped.
    /// </summary>
    Failed = 1,
}

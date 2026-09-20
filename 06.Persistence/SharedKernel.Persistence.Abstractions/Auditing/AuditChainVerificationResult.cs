namespace SharedKernel.Persistence.Abstractions.Auditing;

/// <summary>
/// The outcome of <see cref="IAuditQueryService.VerifyFullChainAsync"/>/
/// <see cref="IAuditQueryService.VerifyChainFromCheckpointAsync"/>.
/// </summary>
/// <remarks>Ordered by <see cref="BrokenAtSequence"/> — a chain's own append-time sequence, not wall-clock time or record identity alone.</remarks>
public sealed record AuditChainVerificationResult
{
    /// <summary>
    /// Gets a value indicating whether every record checked hashes correctly and chains correctly to
    /// its predecessor, with no gap and (when checked against an expected head) no truncation.
    /// </summary>
    public required bool IsIntact { get; init; }

    /// <summary>
    /// Gets the <see cref="AuditRecord.Id"/> of the first record found to be tampered with, missing,
    /// or out of order, or <see langword="null"/> when <see cref="IsIntact"/> is <see langword="true"/>.
    /// </summary>
    public Guid? BrokenAtRecordId { get; init; }

    /// <summary>
    /// Gets the <see cref="AuditRecord.Sequence"/> at which the break was detected — either a record
    /// whose own hash no longer matches, a record whose declared predecessor hash does not match the
    /// previous record actually found, a gap in the sequence, or (for a checkpoint-anchored
    /// verification) the sequence an expected head was never reached. <see langword="null"/> when
    /// <see cref="IsIntact"/> is <see langword="true"/>.
    /// </summary>
    public long? BrokenAtSequence { get; init; }

    /// <summary>Gets a short, human-readable reason for the break, or <see langword="null"/> when <see cref="IsIntact"/> is <see langword="true"/>.</summary>
    public string? Reason { get; init; }

    /// <summary>Gets the number of records examined before the check concluded.</summary>
    public required int RecordsChecked { get; init; }

    /// <summary>Creates a result reporting the chain as fully intact over <paramref name="recordsChecked"/> records.</summary>
    public static AuditChainVerificationResult Intact(int recordsChecked) =>
        new() { IsIntact = true, RecordsChecked = recordsChecked };

    /// <summary>
    /// Creates a result reporting a break at <paramref name="brokenAtSequence"/>/<paramref name="brokenAtRecordId"/>,
    /// having examined <paramref name="recordsChecked"/> records.
    /// </summary>
    public static AuditChainVerificationResult Broken(
        long brokenAtSequence,
        Guid? brokenAtRecordId,
        string reason,
        int recordsChecked) =>
        new()
        {
            IsIntact = false,
            BrokenAtSequence = brokenAtSequence,
            BrokenAtRecordId = brokenAtRecordId,
            Reason = reason,
            RecordsChecked = recordsChecked,
        };
}

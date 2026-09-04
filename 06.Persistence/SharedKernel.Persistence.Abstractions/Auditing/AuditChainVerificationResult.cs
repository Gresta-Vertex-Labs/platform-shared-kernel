namespace SharedKernel.Persistence.Abstractions.Auditing;

/// <summary>
/// The outcome of <see cref="IAuditQueryService.VerifyChainIntegrityAsync"/>.
/// </summary>
/// <remarks>WO-071/P-456/D-119.</remarks>
public sealed record AuditChainVerificationResult
{
    /// <summary>
    /// Gets a value indicating whether every record checked in the requested range hashes correctly
    /// and chains correctly to its predecessor.
    /// </summary>
    public required bool IsIntact { get; init; }

    /// <summary>
    /// Gets the <see cref="AuditRecord.Id"/> of the first record found to be tampered with, missing,
    /// or out of order, or <see langword="null"/> when <see cref="IsIntact"/> is <see langword="true"/>.
    /// </summary>
    public Guid? BrokenAtRecordId { get; init; }

    /// <summary>Gets the number of records examined before the check concluded.</summary>
    public required int RecordsChecked { get; init; }

    /// <summary>Creates a result reporting the chain as fully intact over <paramref name="recordsChecked"/> records.</summary>
    public static AuditChainVerificationResult Intact(int recordsChecked) =>
        new() { IsIntact = true, BrokenAtRecordId = null, RecordsChecked = recordsChecked };

    /// <summary>
    /// Creates a result reporting a break at <paramref name="brokenAtRecordId"/>, having examined
    /// <paramref name="recordsChecked"/> records up to and including that one.
    /// </summary>
    public static AuditChainVerificationResult Broken(Guid brokenAtRecordId, int recordsChecked) =>
        new() { IsIntact = false, BrokenAtRecordId = brokenAtRecordId, RecordsChecked = recordsChecked };
}

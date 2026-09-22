namespace SharedKernel.Persistence.EfCore.Auditing;

/// <summary>The overall verdict of a verification.</summary>
public enum AuditVerificationStatus
{
    /// <summary>Everything checked was proven authentic and complete.</summary>
    Intact = 0,

    /// <summary>Tampering, a gap, a truncation or a key regression was proven.</summary>
    Broken = 1,

    /// <summary>Nothing was proven either way: a key or payload needed for the check is not available.</summary>
    Unverifiable = 2,
}

/// <summary>Why a verification did not report <see cref="AuditVerificationStatus.Intact"/>.</summary>
public enum AuditVerificationFailureKind
{
    /// <summary>No failure.</summary>
    None = 0,

    /// <summary>A chain position is missing or out of order (a record or link was deleted or renumbered).</summary>
    SequenceGap = 1,

    /// <summary>A record's MAC or payload commitment does not match its content (content edited without the key, or payload altered).</summary>
    HashMismatch = 2,

    /// <summary>A record's MAC is valid, but it does not chain to the MAC actually found before it (a record was re-MACed with a leaked key).</summary>
    LinkMismatch = 3,

    /// <summary>A record is sealed under a key older (lower order) than a key already seen earlier in the chain (a forgery with a retired key).</summary>
    KeyRegression = 4,

    /// <summary>The record a checkpoint anchors no longer exists or no longer carries the anchored MAC.</summary>
    AnchorMismatch = 5,

    /// <summary>The chain ends before the sequence an independently signed checkpoint attests to.</summary>
    TailTruncated = 6,

    /// <summary>A record is sealed under a key id or algorithm the configured keyring does not know.</summary>
    UnknownKey = 7,

    /// <summary>A payload the caller required was erased, so its content cannot be verified.</summary>
    PayloadErased = 8,

    /// <summary>The record exists but has not been sealed into its chain yet.</summary>
    NotSealed = 9,
}

/// <summary>The outcome of verifying one chain (or the part of it after a checkpoint).</summary>
/// <remarks>
/// A chain is walked in <c>Sequence</c> order; the first failure stops the walk. Erased payloads do not
/// break a chain (the chain commits to the payload hash, not the payload) and are counted in
/// <see cref="ErasedPayloads"/>, unless the caller asked for payloads to be required.
/// </remarks>
public sealed record AuditChainVerificationResult
{
    /// <summary>Gets the verdict.</summary>
    public required AuditVerificationStatus Status { get; init; }

    /// <summary>Gets the reason for a non-intact verdict, or <see cref="AuditVerificationFailureKind.None"/>.</summary>
    public AuditVerificationFailureKind FailureKind { get; init; }

    /// <summary>Gets the chain position where the walk stopped, or <see langword="null"/> when intact.</summary>
    public long? FailedAtSequence { get; init; }

    /// <summary>Gets the record where the walk stopped, when one was found there.</summary>
    public Guid? FailedAtRecordId { get; init; }

    /// <summary>Gets a short human-readable explanation of the failure, or <see langword="null"/>.</summary>
    public string? Reason { get; init; }

    /// <summary>Gets the number of chained records examined.</summary>
    public long RecordsChecked { get; init; }

    /// <summary>Gets the number of examined records whose payload had been erased.</summary>
    public long ErasedPayloads { get; init; }

    /// <summary>Gets the last sequence proven intact, or <see langword="null"/> when none was.</summary>
    public long? HeadSequence { get; init; }

    /// <summary>Gets a value indicating whether <see cref="Status"/> is <see cref="AuditVerificationStatus.Intact"/>.</summary>
    public bool IsIntact => Status == AuditVerificationStatus.Intact;
}

/// <summary>The outcome of verifying a single record against its own seal.</summary>
public sealed record AuditRecordVerificationResult
{
    /// <summary>Gets the verdict.</summary>
    public required AuditVerificationStatus Status { get; init; }

    /// <summary>Gets the reason for a non-intact verdict, or <see cref="AuditVerificationFailureKind.None"/>.</summary>
    public AuditVerificationFailureKind FailureKind { get; init; }

    /// <summary>Gets a short human-readable explanation of the failure, or <see langword="null"/>.</summary>
    public string? Reason { get; init; }

    /// <summary>Gets a value indicating whether the record's payload had been erased.</summary>
    public bool PayloadErased { get; init; }

    /// <summary>Gets a value indicating whether <see cref="Status"/> is <see cref="AuditVerificationStatus.Intact"/>.</summary>
    public bool IsIntact => Status == AuditVerificationStatus.Intact;
}

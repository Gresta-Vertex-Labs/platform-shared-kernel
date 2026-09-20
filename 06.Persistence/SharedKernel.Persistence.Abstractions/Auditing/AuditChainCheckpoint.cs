namespace SharedKernel.Persistence.Abstractions.Auditing;

/// <summary>
/// A signed, independently-stored anchor recording one chain's head (its latest
/// <see cref="AuditRecord.Sequence"/> and <see cref="AuditRecord.RecordHash"/>) at the moment the
/// checkpoint was created.
/// </summary>
/// <remarks>
/// <para>
/// Optional. A hash chain alone can prove that every record it still holds is internally
/// consistent and correctly ordered, but it cannot, by itself, prove that nothing was deleted from its
/// TAIL — a chain missing its last 50 records looks identical to a chain that genuinely only ever had
/// that many. A checkpoint closes that gap: it is created and signed BEFORE a suspected tampering
/// window, stored outside the chain it anchors, and later verification
/// (<see cref="IAuditQueryService.VerifyChainFromCheckpointAsync"/>) can prove either "the chain still
/// contains, unmodified, everything the checkpoint attested to" or "it does not" — including detecting
/// that the chain no longer reaches as far as the checkpoint claimed.
/// </para>
/// <para>
/// <see cref="Signature"/> is computed over this checkpoint's own fields by
/// <c>01.Core</c>'s <c>IAsymmetricSignatureService</c> — a DIFFERENT key/algorithm family than the
/// chain's own HMAC, so compromising the chain's symmetric HMAC key alone is not sufficient to forge a
/// checkpoint too.
/// </para>
/// </remarks>
public sealed record AuditChainCheckpoint
{
    /// <summary>Gets the unique identifier of this checkpoint.</summary>
    public required Guid Id { get; init; }

    /// <summary>Gets the tenant of the chain this checkpoint anchors, or <see langword="null"/> for a system chain.</summary>
    public Guid? TenantId { get; init; }

    /// <summary>Gets the resource type of the chain this checkpoint anchors.</summary>
    public required string ResourceType { get; init; }

    /// <summary>Gets the anchored chain head's <see cref="AuditRecord.Sequence"/> at checkpoint creation time.</summary>
    public required long Sequence { get; init; }

    /// <summary>Gets the anchored chain head's <see cref="AuditRecord.RecordHash"/> at checkpoint creation time.</summary>
    public required string RecordHash { get; init; }

    /// <summary>Gets when this checkpoint was created.</summary>
    public required DateTimeOffset CreatedOn { get; init; }

    /// <summary>Gets the identifier of the asymmetric signing key that produced <see cref="Signature"/>.</summary>
    public required string SigningKeyId { get; init; }

    /// <summary>Gets the signature over this checkpoint's own fields.</summary>
    public required byte[] Signature { get; init; }
}

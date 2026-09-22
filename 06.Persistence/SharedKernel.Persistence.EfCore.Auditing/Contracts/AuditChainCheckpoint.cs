namespace SharedKernel.Persistence.EfCore.Auditing;

/// <summary>
/// A signed statement "chain <c>(TenantId, ResourceType)</c> had head <see cref="Sequence"/> with MAC
/// <see cref="HeadMac"/> at <see cref="CreatedOn"/>".
/// </summary>
/// <remarks>
/// <para>
/// A chain alone cannot prove that nothing was cut from its tail. A checkpoint stored outside the chain
/// (an <see cref="IAuditCheckpointSink"/>, ideally write-once storage) can: verifying from one
/// checkpoint to a later one detects truncation and tail rewrites.
/// </para>
/// <para>
/// <see cref="Signature"/> is produced by <c>01.Core</c>'s <c>IAsymmetricSignatureService</c> over the
/// AUDITv3 checkpoint encoding (see <c>AUDIT-FORMAT.md</c>) — a different key family than the chain MAC,
/// so a leaked chain key alone cannot forge a checkpoint. Verifiers only accept signatures from
/// <c>AuditLedgerOptions.AcceptedCheckpointSigningKeyIds</c>, never from the key id the checkpoint names.
/// </para>
/// </remarks>
public sealed record AuditChainCheckpoint
{
    /// <summary>Gets the checkpoint identifier.</summary>
    public required Guid Id { get; init; }

    /// <summary>Gets the tenant of the anchored chain, or <see langword="null"/> for the system chain.</summary>
    public Guid? TenantId { get; init; }

    /// <summary>Gets the resource type of the anchored chain.</summary>
    public required string ResourceType { get; init; }

    /// <summary>Gets the anchored head sequence.</summary>
    public required long Sequence { get; init; }

    /// <summary>Gets the anchored head's MAC.</summary>
    public required byte[] HeadMac { get; init; }

    /// <summary>Gets when the checkpoint was created (whole microseconds).</summary>
    public required DateTimeOffset CreatedOn { get; init; }

    /// <summary>Gets the id of the asymmetric key that produced <see cref="Signature"/>.</summary>
    public required string SigningKeyId { get; init; }

    /// <summary>Gets the signature over the checkpoint encoding.</summary>
    public required byte[] Signature { get; init; }
}

/// <summary>
/// Where checkpoints are stored and read back from. The default writes the insert-only
/// <c>audit_checkpoints</c> table; register your own (before <c>WithAuditTrail</c>) to write to
/// WORM/object-lock storage or an external notary.
/// </summary>
public interface IAuditCheckpointSink
{
    /// <summary>Stores <paramref name="checkpoint"/>. Must never overwrite an existing checkpoint.</summary>
    /// <param name="checkpoint">The signed checkpoint.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    Task AppendAsync(AuditChainCheckpoint checkpoint, CancellationToken cancellationToken = default);

    /// <summary>Returns the checkpoint with the highest sequence for the chain, or <see langword="null"/>.</summary>
    /// <param name="tenantId">The chain's tenant, or <see langword="null"/> for the system chain.</param>
    /// <param name="resourceType">The chain's resource type.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    Task<AuditChainCheckpoint?> GetLatestAsync(Guid? tenantId, string resourceType, CancellationToken cancellationToken = default);
}

/// <summary>Creates signed checkpoints on demand. The sealer also emits them periodically.</summary>
public interface IAuditCheckpointService
{
    /// <summary>
    /// Verifies the caller's own <c>(tenant, <paramref name="resourceType"/>)</c> chain from its latest
    /// stored checkpoint to its head, then signs and stores a checkpoint of that head.
    /// </summary>
    /// <param name="resourceType">The chain's resource type.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The new checkpoint.</returns>
    /// <exception cref="AuditChainIntegrityException">The chain is not intact; nothing is signed.</exception>
    /// <exception cref="InvalidOperationException">The chain has no sealed records, or no signing key is configured.</exception>
    Task<AuditChainCheckpoint> CreateCheckpointAsync(string resourceType, CancellationToken cancellationToken = default);

    /// <summary>
    /// Same as <see cref="CreateCheckpointAsync(string, CancellationToken)"/> for an explicit chain.
    /// Requires an active cross-tenant scope unless <paramref name="tenantId"/> is the caller's own tenant.
    /// </summary>
    /// <param name="tenantId">The chain's tenant, or <see langword="null"/> for the system chain.</param>
    /// <param name="resourceType">The chain's resource type.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The new checkpoint.</returns>
    Task<AuditChainCheckpoint> CreateCheckpointForChainAsync(Guid? tenantId, string resourceType, CancellationToken cancellationToken = default);
}

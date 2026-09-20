namespace SharedKernel.Persistence.Abstractions.Auditing;

/// <summary>
/// Creates signed <see cref="AuditChainCheckpoint"/> anchors for a chain's current head.
/// </summary>
/// <remarks>
/// Optional capability — see <see cref="AuditChainCheckpoint"/>'s remarks for what a
/// checkpoint is for. A consuming service calls <see cref="CreateCheckpointAsync"/> periodically (a
/// scheduled job) or at specific compliance-relevant moments (before a data migration, at period-end
/// close) and stores the returned <see cref="AuditChainCheckpoint"/> somewhere <see cref="IAuditQueryService.VerifyChainFromCheckpointAsync"/>
/// can later read it back from — this package does not itself decide where checkpoints are archived.
/// </remarks>
public interface IAuditCheckpointService
{
    /// <summary>
    /// Creates and signs a checkpoint anchoring the CURRENT head of the <c>(tenantId, resourceType)</c> chain.
    /// </summary>
    /// <param name="tenantId">The tenant partition to checkpoint, or <see langword="null"/> for the system chain.</param>
    /// <param name="resourceType">The resource-type partition to checkpoint.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The new, signed checkpoint.</returns>
    /// <exception cref="InvalidOperationException">The chain has no records yet.</exception>
    Task<AuditChainCheckpoint> CreateCheckpointAsync(
        Guid? tenantId,
        string resourceType,
        CancellationToken cancellationToken = default);
}

namespace SharedKernel.Persistence.Abstractions.Auditing;

/// <summary>
/// Read-side queries over the append-only audit trail written by <see cref="IAuditTrailWriter"/>.
/// </summary>
/// <remarks>
/// <para>
/// WO-071/P-456/D-117/D-120. Deliberately NOT built on <c>IReadRepository&lt;TAggregate,TId&gt;</c>:
/// that contract is constrained <c>where TAggregate : IAggregateRoot{TId}</c>, and
/// <c>IAggregateRoot{TId}</c> itself extends <c>IHasDomainEvents</c> — forcing a plain infrastructure
/// record like <see cref="AuditRecord"/> to carry an always-empty domain-events collection purely to
/// satisfy a generic constraint would be an unjustified stretch.
/// The EF Core implementation builds directly on the lower-level, unconstrained
/// <c>ISpecificationEvaluator{T}</c> instead, reusing the exact same keyset-seek-predicate
/// translation any <c>KeysetSpecification&lt;T,TKey&gt;</c> already gets (P-317/WO-051).
/// </para>
/// </remarks>
public interface IAuditQueryService
{
    /// <summary>
    /// Retrieves the audit history for a single resource, keyset/cursor-paginated.
    /// </summary>
    /// <param name="specification">The resource-history query.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    Task<IReadOnlyList<AuditRecord>> GetResourceHistoryAsync(
        AuditResourceHistorySpecification specification,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves everything a single actor has done, keyset/cursor-paginated.
    /// </summary>
    /// <param name="specification">The actor-actions query.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    Task<IReadOnlyList<AuditRecord>> GetActorActionsAsync(
        AuditActorActionsSpecification specification,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies the hash chain for the <c>(tenantId, resourceType)</c> partition over
    /// <c>[from, to]</c>, detecting any tampered, missing, or reordered record.
    /// </summary>
    /// <param name="tenantId">The tenant partition to verify.</param>
    /// <param name="resourceType">The resource-type partition to verify.</param>
    /// <param name="from">The inclusive start of the <see cref="AuditRecord.OccurredOn"/> range to check.</param>
    /// <param name="to">The inclusive end of the <see cref="AuditRecord.OccurredOn"/> range to check.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    Task<AuditChainVerificationResult> VerifyChainIntegrityAsync(
        Guid tenantId,
        string resourceType,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);
}

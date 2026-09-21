using SharedKernel.Contracts.Pagination;
using SharedKernel.Application.Auditing;
using SharedKernel.Application.Context;
using SharedKernel.Persistence.Abstractions.Context;

namespace SharedKernel.Persistence.Abstractions.Auditing;

/// <summary>
/// Read-side queries over the append-only audit trail written by <see cref="IAuditTrailWriter"/>.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately NOT built on
/// <c>IReadRepository&lt;TAggregate,TId&gt;</c>: that contract is constrained
/// <c>where TAggregate : IAggregateRoot{TId}</c>, and <c>IAggregateRoot{TId}</c> itself extends
/// <c>IHasDomainEvents</c> — forcing a plain infrastructure record like <see cref="AuditRecord"/> to
/// carry an always-empty domain-events collection purely to satisfy a generic constraint would be an
/// unjustified stretch. The EF Core implementation builds directly on the lower-level, unconstrained
/// <c>ISpecificationEvaluator{T}</c> instead.
/// </para>
/// <para>
/// <strong>Tenant scoping — every method except the two explicitly named "AcrossTenants" ones resolves
/// its tenant from the caller's own <see cref="IRequestContext"/></strong>, never from a
/// caller-supplied parameter that could be forged to read another tenant's history. The two
/// cross-tenant methods are separately named specifically so a call site cannot reach cross-tenant data
/// by accident, and the implementation additionally requires an active <see cref="ICrossTenantScope"/>
/// — see their own remarks.
/// </para>
/// <para>
/// <strong>Page size:</strong> every keyset method's page size is capped at
/// <see cref="AuditQueryLimits.MaxPageSize"/>, enforced by <see cref="AuditResourceHistorySpecification"/>/
/// <see cref="AuditActorActionsSpecification"/>'s own constructors — this service never receives a
/// specification whose declared page size could exceed the cap.
/// </para>
/// </remarks>
public interface IAuditQueryService
{
    /// <summary>
    /// Retrieves the audit history for a single resource within the caller's own tenant, keyset/cursor-paginated.
    /// </summary>
    /// <param name="specification">The resource-history query.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    Task<CursorPagedList<AuditRecord>> GetResourceHistoryAsync(
        AuditResourceHistorySpecification specification,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves everything a single actor has done within the caller's own tenant, keyset/cursor-paginated.
    /// </summary>
    /// <param name="specification">The actor-actions query.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    Task<CursorPagedList<AuditRecord>> GetActorActionsAsync(
        AuditActorActionsSpecification specification,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves the audit history for a single resource ACROSS EVERY TENANT — a privileged,
    /// separately-named operation for a support/admin cross-tenant investigation.
    /// </summary>
    /// <param name="resourceType">The resource type to filter by.</param>
    /// <param name="resourceId">The specific resource instance to filter by.</param>
    /// <param name="afterId">
    /// The <see cref="AuditRecord.Id"/> of the last row seen on the previous page, or
    /// <see langword="null"/> for the first page. <see cref="AuditRecord.Id"/> (a
    /// <see cref="Guid.CreateVersion7()"/> value, roughly time-ordered) is used as the sole cursor key
    /// here because <see cref="AuditRecord.Sequence"/> is unique only WITHIN one tenant's chain — a
    /// cross-tenant page necessarily interleaves several chains, each with its own independent
    /// sequence numbering, so <see cref="AuditRecord.Sequence"/> cannot order a mixed-tenant result
    /// set on its own.
    /// </param>
    /// <param name="descending">
    /// <see langword="true"/> to return the most recently created records first; <see langword="false"/>
    /// for creation order.
    /// </param>
    /// <param name="take">The maximum number of records to return. Must be between 1 and <see cref="AuditQueryLimits.MaxPageSize"/>.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <exception cref="InvalidOperationException">No <see cref="ICrossTenantScope"/> is active for the current logical call.</exception>
    /// <remarks>
    /// Requires an active <see cref="ICrossTenantScope"/> — call
    /// <c>using var _ = crossTenantScope.Enter();</c> around this call to make the bypass explicit and
    /// attributable, mirroring <c>TenantedRepository</c>'s existing cross-tenant read methods.
    /// </remarks>
    Task<CursorPagedList<AuditRecord>> GetResourceHistoryAcrossTenantsAsync(
        string resourceType,
        string resourceId,
        Guid? afterId,
        bool descending,
        int take,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Streams every record in the CALLER'S OWN <c>(TenantId, ResourceType)</c> chain whose
    /// <see cref="AuditRecord.OccurredOn"/> falls within <c>[from, to]</c>, in
    /// <see cref="AuditRecord.Sequence"/> order, without buffering the whole range in memory.
    /// </summary>
    /// <param name="resourceType">The resource-type partition to export.</param>
    /// <param name="from">The inclusive start of the <see cref="AuditRecord.OccurredOn"/> range.</param>
    /// <param name="to">The inclusive end of the <see cref="AuditRecord.OccurredOn"/> range.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <remarks>
    /// The tenant is resolved from the caller's own <see cref="IRequestContext"/> — see type-level
    /// remarks; there is deliberately no <c>tenantId</c> parameter here to forge. A system/background
    /// caller (<see cref="IRequestContext.TenantId"/> <see langword="null"/>) exports the system
    /// chain.
    /// </remarks>
    IAsyncEnumerable<AuditRecord> ExportRangeAsync(
        string resourceType,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies the ENTIRE <c>(TenantId, resourceType)</c> chain for the CALLER'S OWN tenant, from its
    /// first record (<see cref="AuditRecord.Sequence"/> 1) to its current head, detecting any tampered
    /// record, any gap (a deleted or reordered record), and any broken chain link.
    /// </summary>
    /// <param name="resourceType">The resource-type partition to verify.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <remarks>
    /// <para>
    /// The tenant is resolved from the caller's own <see cref="IRequestContext"/> — see
    /// type-level remarks; there is deliberately no <c>tenantId</c> parameter here to forge. A
    /// system/background caller (<see cref="IRequestContext.TenantId"/> <see langword="null"/>)
    /// verifies the system chain.
    /// </para>
    /// <para>
    /// <strong>Cannot detect tail truncation on its own</strong> — a chain missing its last N records
    /// looks identical to a chain that genuinely only ever had that many; nothing on record proves a
    /// later record ever existed. Detecting that requires an independently-stored, previously-created
    /// <see cref="AuditChainCheckpoint"/> — see <see cref="VerifyChainFromCheckpointAsync"/>.
    /// </para>
    /// </remarks>
    Task<AuditChainVerificationResult> VerifyFullChainAsync(
        string resourceType,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies a chain starting from a previously-created, independently-stored
    /// <see cref="AuditChainCheckpoint"/> rather than from the chain's genesis — both cheaper (it never
    /// re-walks records already proven intact when the checkpoint was made) and, unlike
    /// <see cref="VerifyFullChainAsync"/>, able to detect tail truncation when
    /// <paramref name="expectedHead"/> is supplied.
    /// </summary>
    /// <param name="checkpoint">The checkpoint to verify from. Its own signature is checked first.</param>
    /// <param name="expectedHead">
    /// An optional, more RECENT checkpoint for the same chain. When supplied, this call additionally
    /// fails if the chain does not actually reach <paramref name="expectedHead"/>'s recorded sequence
    /// and hash — detecting that records were deleted from the chain's tail after
    /// <paramref name="expectedHead"/> was created.
    /// </param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <exception cref="ArgumentException"><paramref name="checkpoint"/>'s signature does not verify, or <paramref name="expectedHead"/> names a different chain.</exception>
    Task<AuditChainVerificationResult> VerifyChainFromCheckpointAsync(
        AuditChainCheckpoint checkpoint,
        AuditChainCheckpoint? expectedHead,
        CancellationToken cancellationToken = default);
}

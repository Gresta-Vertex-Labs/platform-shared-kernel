using SharedKernel.Contracts.Pagination;

namespace SharedKernel.Persistence.EfCore.Auditing;

/// <summary>Reads and verifies the audit ledger.</summary>
/// <remarks>
/// <para>
/// Every method resolves the tenant from the caller's <c>IRequestContext</c>; no method takes a tenant a
/// caller could forge. The system chain (no tenant) is reachable only by an authenticated
/// <c>ActorKind.System</c> identity or inside an active cross-tenant scope (A16). The methods named
/// "AcrossTenants" require an active cross-tenant scope.
/// </para>
/// <para>
/// <see cref="ExportRangeAsync"/> and <see cref="QueryAcrossTenantsAsync"/> write their own audit record
/// before returning data (<see cref="AuditLedgerActions"/>).
/// </para>
/// </remarks>
public interface IAuditQueryService
{
    /// <summary>Returns one page of the caller's own tenant's records matching <paramref name="query"/>.</summary>
    /// <param name="query">The query.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <exception cref="ArgumentException">The query shape or limit is not supported, or the cursor is invalid.</exception>
    Task<CursorPagedList<AuditRecord>> QueryAsync(AuditRecordQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns one page of records for one resource across every tenant. Requires an active cross-tenant
    /// scope and <see cref="AuditRecordQuery.ResourceType"/> plus <see cref="AuditRecordQuery.ResourceId"/>;
    /// records an <see cref="AuditLedgerActions.CrossTenantQueried"/> entry first.
    /// </summary>
    /// <param name="query">The query.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <exception cref="InvalidOperationException">No cross-tenant scope is active.</exception>
    Task<CursorPagedList<AuditRecord>> QueryAcrossTenantsAsync(AuditRecordQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Streams the caller's own chain for <paramref name="resourceType"/> between <paramref name="from"/>
    /// and <paramref name="to"/> (inclusive), in <c>OccurredOn</c> order, after recording an
    /// <see cref="AuditLedgerActions.Exported"/> entry.
    /// </summary>
    /// <param name="resourceType">The chain's resource type.</param>
    /// <param name="from">Inclusive lower bound.</param>
    /// <param name="to">Inclusive upper bound.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    IAsyncEnumerable<AuditRecord> ExportRangeAsync(string resourceType, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies the caller's own chain for <paramref name="resourceType"/> from sequence 1 to its sealed
    /// head. Cannot detect a truncated tail on its own — use <see cref="VerifyChainFromCheckpointAsync"/>.
    /// </summary>
    /// <param name="resourceType">The chain's resource type.</param>
    /// <param name="requirePayloads">
    /// <see langword="true"/> to report <see cref="AuditVerificationStatus.Unverifiable"/> /
    /// <see cref="AuditVerificationFailureKind.PayloadErased"/> at the first erased payload instead of
    /// counting it in <see cref="AuditChainVerificationResult.ErasedPayloads"/>.
    /// </param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    Task<AuditChainVerificationResult> VerifyChainAsync(string resourceType, bool requirePayloads = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies a chain from a signed checkpoint: the anchor record is re-authenticated (not just compared),
    /// every later record is walked, and — when <paramref name="expectedHead"/> is given — the chain must
    /// still reach that later checkpoint's sequence with the same MAC.
    /// </summary>
    /// <param name="checkpoint">The checkpoint to start from.</param>
    /// <param name="expectedHead">An optional later checkpoint of the same chain.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <exception cref="ArgumentException">
    /// A checkpoint's signature does not verify, it is signed by a key id not in
    /// <c>AcceptedCheckpointSigningKeyIds</c>, or the two checkpoints name different chains.
    /// </exception>
    /// <exception cref="InvalidOperationException">The checkpoint's chain is not the caller's and no cross-tenant scope is active.</exception>
    Task<AuditChainVerificationResult> VerifyChainFromCheckpointAsync(
        AuditChainCheckpoint checkpoint,
        AuditChainCheckpoint? expectedHead = null,
        CancellationToken cancellationToken = default);

    /// <summary>Verifies one record's MAC and payload commitment against its seal.</summary>
    /// <param name="recordId">The record.</param>
    /// <param name="requirePayload"><see langword="true"/> to report an erased payload as <see cref="AuditVerificationFailureKind.PayloadErased"/>.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The result, or <see langword="null"/> when the caller's tenant holds no such record.</returns>
    Task<AuditRecordVerificationResult?> VerifyRecordAsync(Guid recordId, bool requirePayload = false, CancellationToken cancellationToken = default);
}

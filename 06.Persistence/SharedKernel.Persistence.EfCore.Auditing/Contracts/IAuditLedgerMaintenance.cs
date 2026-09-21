namespace SharedKernel.Persistence.EfCore.Auditing;

/// <summary>Operational commands on the ledger: sealing on demand, erasure, and recovery after a key compromise.</summary>
public interface IAuditLedgerMaintenance
{
    /// <summary>
    /// Runs sealing passes until nothing sealable is left (or another instance holds the sealer lock).
    /// The background sealer does the same on its interval; this is for tests, tooling and shutdown hooks.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    Task<AuditSealPassResult> SealPendingAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Appends an <see cref="AuditLedgerActions.ChainResealed"/> record to every chain, seals them under
    /// the current key and emits a checkpoint of every chain head (when a checkpoint signing key is
    /// configured). Run it right after rotating away from a compromised key: any record later forged
    /// with the old key is then detected as <see cref="AuditVerificationFailureKind.KeyRegression"/>.
    /// Requires an active cross-tenant scope.
    /// </summary>
    /// <param name="reason">Why the chains are being resealed; stored in every marker record.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <exception cref="InvalidOperationException">No cross-tenant scope is active.</exception>
    Task<AuditResealResult> SealAllChainsAsync(string reason, CancellationToken cancellationToken = default);

    /// <summary>
    /// Erases one record's payload (snapshots and salt) and records an
    /// <see cref="AuditLedgerActions.PayloadErased"/> entry in the same transaction. The chain stays
    /// verifiable. The record must belong to the caller's tenant unless a cross-tenant scope is active.
    /// </summary>
    /// <param name="recordId">The record.</param>
    /// <param name="reason">The legal reason (e.g. a GDPR/KVKK request id).</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns><see langword="true"/> when a payload was erased; <see langword="false"/> when it was already erased.</returns>
    /// <exception cref="KeyNotFoundException">The caller's tenant holds no such record.</exception>
    Task<bool> ErasePayloadAsync(Guid recordId, string reason, CancellationToken cancellationToken = default);

    /// <summary>
    /// Erases the payloads of every record of one resource in the caller's tenant (a data-subject erasure),
    /// recording one <see cref="AuditLedgerActions.PayloadErased"/> entry for the whole operation.
    /// </summary>
    /// <param name="resourceType">The resource type.</param>
    /// <param name="resourceId">The resource instance.</param>
    /// <param name="reason">The legal reason.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The number of payloads erased.</returns>
    Task<int> EraseResourcePayloadsAsync(string resourceType, string resourceId, string reason, CancellationToken cancellationToken = default);
}

/// <summary>The outcome of <see cref="IAuditLedgerMaintenance.SealPendingAsync"/>.</summary>
/// <param name="LockAcquired"><see langword="false"/> when another instance held the sealer lock.</param>
/// <param name="RecordsSealed">The number of records sealed.</param>
public sealed record AuditSealPassResult(bool LockAcquired, int RecordsSealed);

/// <summary>The outcome of <see cref="IAuditLedgerMaintenance.SealAllChainsAsync"/>.</summary>
/// <param name="ChainsResealed">The number of chains that received a marker record.</param>
/// <param name="RecordsSealed">The number of records sealed while resealing.</param>
/// <param name="CheckpointsEmitted">The number of checkpoints emitted afterwards.</param>
/// <param name="FullySealed">
/// <see langword="false"/> when some marker could not be sealed yet (an older transaction was still open);
/// the background sealer finishes the job.
/// </param>
public sealed record AuditResealResult(int ChainsResealed, int RecordsSealed, int CheckpointsEmitted, bool FullySealed);

/// <summary>Readiness primitive reporting how far the sealer lags behind the writers. No <c>IHealthCheck</c> is shipped.</summary>
public interface IAuditSealingProbe
{
    /// <summary>Measures the unsealed tail of the ledger.</summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    Task<AuditSealingHealth> ProbeAsync(CancellationToken cancellationToken = default);
}

/// <summary>The unsealed tail of the ledger at probe time.</summary>
/// <param name="UnsealedRecords">Committed records not yet sealed.</param>
/// <param name="OldestUnsealedOccurredOn">When the oldest of them was written, or <see langword="null"/>.</param>
/// <param name="Lag">Now minus <paramref name="OldestUnsealedOccurredOn"/>, or <see cref="TimeSpan.Zero"/>.</param>
public sealed record AuditSealingHealth(long UnsealedRecords, DateTimeOffset? OldestUnsealedOccurredOn, TimeSpan Lag);

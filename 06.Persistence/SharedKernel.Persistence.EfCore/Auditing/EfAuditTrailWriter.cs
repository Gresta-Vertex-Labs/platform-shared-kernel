using Microsoft.EntityFrameworkCore;
using SharedKernel.Cryptography.Hashing;
using SharedKernel.Persistence.Abstractions.Auditing;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Primitives.Clocks;

namespace SharedKernel.Persistence.EfCore.Auditing;

/// <summary>
/// EF Core implementation of <see cref="IAuditTrailWriter"/>.
/// </summary>
/// <remarks>
/// <para>
/// WO-071/P-457/D-122. <see cref="RecordAsync"/> is SELF-CONTAINED — it calls
/// <see cref="DbContext.SaveChangesAsync(CancellationToken)"/> on the injected
/// <see cref="SharedKernelDbContext"/> itself, immediately, rather than staging the new
/// <see cref="AuditRecord"/> into the ambient <c>DbContext</c>/<c>IUnitOfWork</c> alongside
/// unrelated business-entity changes. Audit correctness must never be contingent on whether, or
/// when, the caller's own unrelated <c>IUnitOfWork.SaveChangesAsync()</c> eventually fires.
/// </para>
/// <para>
/// <strong>Concurrency (documented, not fully closed):</strong> <see cref="PreviousRecordHash"/>
/// resolution reads the latest record's hash for the <c>(TenantId, ResourceType)</c> partition,
/// then writes the new record chained to it, as two sequential statements. Under the default READ
/// COMMITTED isolation most relational providers use, two truly concurrent
/// <see cref="RecordAsync"/> calls against the SAME partition could both read the same
/// "latest" hash and both succeed, forking the chain (both new records would carry the same
/// <see cref="AuditRecord.PreviousRecordHash"/>) — <see cref="IAuditQueryService.VerifyChainIntegrityAsync"/>
/// would then detect exactly one of the two as broken (whichever sorts second by
/// <c>(OccurredOn, Id)</c>). Closing this race fully would require SERIALIZABLE isolation or an
/// advisory lock scoped to the partition — a genuine future hardening opportunity, out of scope for
/// this phase; audit writes for a single resource in ordinary application code are not, in practice,
/// issued concurrently against the SAME partition.
/// </para>
/// <para>
/// <strong>Same-millisecond tiebreak (documented, not fully closed):</strong> when two records in
/// the SAME partition share an identical <see cref="AuditRecord.OccurredOn"/> (real
/// millisecond-resolution wall-clock ties under bursty load), <c>ORDER BY (OccurredOn, Id)</c> falls
/// back to comparing <see cref="AuditRecord.Id"/> — a <see cref="Guid.CreateVersion7"/> value.
/// CONFIRMED EMPIRICALLY while building this class: two <see cref="Guid.CreateVersion7"/> values
/// generated within the same millisecond do NOT reliably compare in generation order (their
/// sub-millisecond bits are cryptographically random, not a monotonic counter) — roughly half of
/// same-millisecond pairs sort opposite to their true creation order, under both
/// <see cref="Guid.CompareTo(Guid)"/> and byte-lexicographic comparison. In that rare case this
/// writer could chain a new record to a slightly-earlier "latest" than the true most-recent one —
/// the hash chain itself remains internally CONSISTENT (every <see cref="AuditRecord.PreviousRecordHash"/>
/// still points to a real, correctly-hashed prior record, so <c>VerifyChainIntegrityAsync</c> still
/// reports it intact), only the WALL-CLOCK "latest" selection is approximate in this narrow window.
/// Closing this fully would mean not relying on <see cref="Guid"/> comparison for chronological
/// tiebreaking anywhere records can tie at millisecond resolution — a platform-wide concern shared by
/// every <c>KeysetSpecification&lt;T,TKey&gt;</c> consumer using a <see cref="Guid.CreateVersion7"/>
/// tiebreaker (P-308/WO-051), not something introduced or fixable locally in this class.
/// </para>
/// </remarks>
public sealed class EfAuditTrailWriter : IAuditTrailWriter
{
    private readonly SharedKernelDbContext _dbContext;
    private readonly IClock _clock;
    private readonly IContentHasher _contentHasher;
    private readonly IAuditActorContext _actorContext;

    /// <summary>
    /// Initialises a new <see cref="EfAuditTrailWriter"/>.
    /// </summary>
    /// <param name="dbContext">The <see cref="SharedKernelDbContext"/> to write audit records against.</param>
    /// <param name="clock">Abstracted system clock — the sole source of <see cref="AuditRecord.OccurredOn"/>.</param>
    /// <param name="contentHasher">
    /// Fast, non-secret content hasher (<c>01.Core/SharedKernel.Cryptography</c>) used to compute
    /// each record's hash-chain digest.
    /// </param>
    /// <param name="actorContext">Resolves the current actor and tenant identity.</param>
    public EfAuditTrailWriter(
        SharedKernelDbContext dbContext,
        IClock clock,
        IContentHasher contentHasher,
        IAuditActorContext actorContext)
    {
        _dbContext = dbContext;
        _clock = clock;
        _contentHasher = contentHasher;
        _actorContext = actorContext;
    }

    /// <inheritdoc />
    public async Task<AuditRecord> RecordAsync(AuditEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var tenantId = _actorContext.TenantId;
        var actorId = _actorContext.ActorId;
        var occurredOn = _clock.UtcNow;
        var id = Guid.CreateVersion7();

        var previousRecordHash = await _dbContext.Set<AuditRecord>()
            .Where(r => r.TenantId == tenantId && r.ResourceType == entry.ResourceType)
            .OrderByDescending(r => r.OccurredOn)
            .ThenByDescending(r => r.Id)
            .Select(r => r.RecordHash)
            .FirstOrDefaultAsync(cancellationToken);

        var recordHash = AuditRecordHasher.ComputeHashHex(
            _contentHasher,
            id,
            tenantId,
            actorId,
            entry.Action,
            entry.ResourceType,
            entry.ResourceId,
            occurredOn,
            entry.BeforeSnapshot,
            entry.AfterSnapshot,
            entry.CorrelationId,
            entry.ApprovalId,
            previousRecordHash);

        var record = new AuditRecord
        {
            Id = id,
            TenantId = tenantId,
            ActorId = actorId,
            Action = entry.Action,
            ResourceType = entry.ResourceType,
            ResourceId = entry.ResourceId,
            OccurredOn = occurredOn,
            BeforeSnapshot = entry.BeforeSnapshot,
            AfterSnapshot = entry.AfterSnapshot,
            CorrelationId = entry.CorrelationId,
            ApprovalId = entry.ApprovalId,
            RecordHash = recordHash,
            PreviousRecordHash = previousRecordHash,
        };

        _dbContext.Set<AuditRecord>().Add(record);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return record;
    }
}

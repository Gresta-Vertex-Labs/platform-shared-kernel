using Microsoft.EntityFrameworkCore;
using SharedKernel.Cryptography.Hashing;
using SharedKernel.Persistence.Abstractions.Auditing;
using SharedKernel.Persistence.Abstractions.Specifications;
using SharedKernel.Persistence.EfCore.Context;

namespace SharedKernel.Persistence.EfCore.Auditing;

/// <summary>
/// EF Core implementation of <see cref="IAuditQueryService"/>.
/// </summary>
/// <remarks>
/// <para>
/// WO-071/P-457/D-124. Built DIRECTLY on <see cref="ISpecificationEvaluator{T}"/> +
/// <see cref="DbContext.Set{TEntity}"/>, NOT <c>IReadRepository&lt;AuditRecord,Guid&gt;</c> — see
/// <see cref="AuditRecord"/>'s remarks (D-120) for why.
/// </para>
/// <para>
/// <strong>CORRECTED against the root phase's own framing (P-456):</strong>
/// <see cref="GetResourceHistoryAsync"/>/<see cref="GetActorActionsAsync"/> call
/// <see cref="ISpecificationEvaluator{T}.GetKeysetQuery{TKey}"/>, NOT <c>GetQuery</c>. Per
/// <see cref="ISpecificationEvaluator{T}"/>'s own documented "Hard constraint": passing a
/// <c>KeysetSpecification&lt;T,TKey&gt;</c> to <c>GetQuery</c> compiles and runs, but SILENTLY
/// IGNORES <c>AfterKey</c>/<c>AfterId</c> and always returns the first page — only
/// <c>GetKeysetQuery</c> honors the cursor. Confirmed against the real
/// <c>SpecificationEvaluator&lt;T&gt;</c> source before writing this class.
/// </para>
/// </remarks>
public sealed class EfAuditQueryService : IAuditQueryService
{
    private readonly SharedKernelDbContext _dbContext;
    private readonly ISpecificationEvaluator<AuditRecord> _evaluator;
    private readonly IContentHasher _contentHasher;

    /// <summary>
    /// Initialises a new <see cref="EfAuditQueryService"/>.
    /// </summary>
    /// <param name="dbContext">The <see cref="SharedKernelDbContext"/> to query audit records from.</param>
    /// <param name="evaluator">The specification evaluator translating keyset specifications into queries.</param>
    /// <param name="contentHasher">The same content hasher used by <see cref="EfAuditTrailWriter"/>, for chain verification.</param>
    public EfAuditQueryService(
        SharedKernelDbContext dbContext,
        ISpecificationEvaluator<AuditRecord> evaluator,
        IContentHasher contentHasher)
    {
        _dbContext = dbContext;
        _evaluator = evaluator;
        _contentHasher = contentHasher;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AuditRecord>> GetResourceHistoryAsync(
        AuditResourceHistorySpecification specification,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(specification);

        var query = _evaluator.GetKeysetQuery(_dbContext.Set<AuditRecord>().AsQueryable(), specification);
        return await query.ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AuditRecord>> GetActorActionsAsync(
        AuditActorActionsSpecification specification,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(specification);

        var query = _evaluator.GetKeysetQuery(_dbContext.Set<AuditRecord>().AsQueryable(), specification);
        return await query.ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<AuditChainVerificationResult> VerifyChainIntegrityAsync(
        Guid tenantId,
        string resourceType,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        var records = _dbContext.Set<AuditRecord>()
            .Where(r =>
                r.TenantId == tenantId &&
                r.ResourceType == resourceType &&
                r.OccurredOn >= from &&
                r.OccurredOn <= to)
            .OrderBy(r => r.OccurredOn)
            .ThenBy(r => r.Id)
            .AsAsyncEnumerable();

        var recordsChecked = 0;
        string? previousRecordActualHash = null;
        var isFirstInRange = true;

        await foreach (var record in records.WithCancellation(cancellationToken))
        {
            recordsChecked++;

            var recomputedHash = AuditRecordHasher.ComputeHashHex(
                _contentHasher,
                record.Id,
                record.TenantId,
                record.ActorId,
                record.Action,
                record.ResourceType,
                record.ResourceId,
                record.OccurredOn,
                record.BeforeSnapshot,
                record.AfterSnapshot,
                record.CorrelationId,
                record.ApprovalId,
                record.PreviousRecordHash);

            // Detects a mutated record: its stored hash no longer matches its own recomputed digest.
            if (!string.Equals(recomputedHash, record.RecordHash, StringComparison.Ordinal))
                return AuditChainVerificationResult.Broken(record.Id, recordsChecked);

            // Detects a missing/reordered link WITHIN the scanned range: this record's declared
            // predecessor must match the hash of the record immediately preceding it in
            // (OccurredOn, Id) order among the records this call examined. The very first record in
            // the range has no in-range predecessor to compare against — whatever it links to
            // outside [from, to] is not this call's concern (its own tamper check above still runs).
            if (!isFirstInRange &&
                !string.Equals(record.PreviousRecordHash, previousRecordActualHash, StringComparison.Ordinal))
            {
                return AuditChainVerificationResult.Broken(record.Id, recordsChecked);
            }

            isFirstInRange = false;
            previousRecordActualHash = record.RecordHash;
        }

        return AuditChainVerificationResult.Intact(recordsChecked);
    }
}

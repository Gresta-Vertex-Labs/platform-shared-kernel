using SharedKernel.Domain.Specifications;
using SharedKernel.Persistence.Abstractions.Auditing;

namespace SharedKernel.Testing.Persistence;

/// <summary>
/// In-memory fake implementation of <see cref="IAuditQueryService"/> (<c>06.Persistence.Abstractions</c>)
/// for use in unit tests, reading from a <see cref="FakeAuditTrailWriter"/>'s recorded audit trail.
/// </summary>
/// <remarks>
/// <para>
/// Composes with a <see cref="FakeAuditTrailWriter"/> instance — mirroring the real production shape,
/// where a writer and a query service both operate against the same underlying store: a test
/// constructs <c>var writer = new FakeAuditTrailWriter(); var queryService = new FakeAuditQueryService(writer);</c>.
/// </para>
/// <para>
/// <see cref="GetResourceHistoryAsync"/>/<see cref="GetActorActionsAsync"/> evaluate the supplied
/// <see cref="KeysetSpecification{T, TKey}"/> in-memory — filter (<c>Criteria</c>), then order
/// (<c>OrderBy</c>/<c>OrderByDescending</c> + <c>ThenBys</c>), then a cursor seek
/// (<c>AfterKey</c>/<c>AfterId</c>/<c>Descending</c>), then <c>Take</c> — mirroring
/// <c>Persistence/FakeRepository{TAggregate,TId}.ListKeysetAsync</c>'s identical, already-proven
/// technique exactly (same folder, not a sibling-isolation violation).
/// </para>
/// <para>
/// <see cref="VerifyChainIntegrityAsync"/> is a deliberate TEST-DOUBLE SIMPLIFICATION, documented
/// explicitly: it validates hash-chain continuity only WITHIN the requested
/// <c>[from, to]</c> window (the first record examined is not required to chain to a predecessor
/// outside the window) — a real implementation would validate against the FULL partition history.
/// This is sufficient to prove the tamper-detection contract in a test that seeds a small, self-
/// contained chain via <see cref="FakeAuditTrailWriter.Seed"/>.
/// </para>
/// </remarks>
public sealed class FakeAuditQueryService : IAuditQueryService
{
    private readonly FakeAuditTrailWriter _writer;

    /// <summary>Initialises a new <see cref="FakeAuditQueryService"/> reading from <paramref name="writer"/>.</summary>
    /// <param name="writer">The writer whose recorded audit trail this query service reads.</param>
    public FakeAuditQueryService(FakeAuditTrailWriter writer) =>
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));

    /// <inheritdoc />
    public Task<IReadOnlyList<AuditRecord>> GetResourceHistoryAsync(
        AuditResourceHistorySpecification specification,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(specification);
        return Task.FromResult(EvaluateKeyset(_writer.Records, specification));
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<AuditRecord>> GetActorActionsAsync(
        AuditActorActionsSpecification specification,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(specification);
        return Task.FromResult(EvaluateKeyset(_writer.Records, specification));
    }

    /// <inheritdoc />
    public Task<AuditChainVerificationResult> VerifyChainIntegrityAsync(
        Guid tenantId,
        string resourceType,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resourceType);

        var chain = _writer.Records
            .Where(r => r.TenantId == tenantId && r.ResourceType == resourceType && r.OccurredOn >= from && r.OccurredOn <= to)
            .OrderBy(r => r.OccurredOn)
            .ThenBy(r => r.Id)
            .ToList();

        string? expectedPreviousHash = null;
        var recordsChecked = 0;

        foreach (var record in chain)
        {
            recordsChecked++;

            if (!string.Equals(record.RecordHash, FakeAuditTrailWriter.ComputeHash(record), StringComparison.Ordinal))
            {
                return Task.FromResult(AuditChainVerificationResult.Broken(record.Id, recordsChecked));
            }

            if (recordsChecked > 1 && !string.Equals(record.PreviousRecordHash, expectedPreviousHash, StringComparison.Ordinal))
            {
                return Task.FromResult(AuditChainVerificationResult.Broken(record.Id, recordsChecked));
            }

            expectedPreviousHash = record.RecordHash;
        }

        return Task.FromResult(AuditChainVerificationResult.Intact(recordsChecked));
    }

    private static IReadOnlyList<AuditRecord> EvaluateKeyset<TKey>(
        IReadOnlyList<AuditRecord> source,
        KeysetSpecification<AuditRecord, TKey> spec)
        where TKey : struct, IComparable<TKey>
    {
        IEnumerable<AuditRecord> query = source;

        if (spec.Criteria is not null)
        {
            query = query.Where(spec.Criteria.Compile());
        }

        var keySelector = (spec.OrderBy ?? spec.OrderByDescending)!.Compile();
        var idSelector = spec.ThenBys[0].KeySelector.Compile();

        var ordered = spec.Descending
            ? query.OrderByDescending(keySelector).ThenBy(idSelector)
            : query.OrderBy(keySelector).ThenBy(idSelector);

        IEnumerable<AuditRecord> afterCursor = ordered;
        if (spec.AfterKey is { } afterKey)
        {
            var afterId = spec.AfterId;
            afterCursor = ordered.SkipWhile(item =>
            {
                var itemKey = (TKey)keySelector(item);
                var primaryRank = itemKey.CompareTo(afterKey);
                if (spec.Descending)
                {
                    primaryRank = -primaryRank;
                }

                if (primaryRank != 0)
                {
                    return primaryRank < 0;
                }

                return Comparer<object>.Default.Compare(idSelector(item), afterId) <= 0;
            });
        }

        return [.. afterCursor.Take(spec.Take!.Value)];
    }
}

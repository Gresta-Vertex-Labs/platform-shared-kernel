using SharedKernel.Contracts.Pagination;
using SharedKernel.Domain.Specifications;
using SharedKernel.Persistence.Abstractions.Auditing;
using SharedKernel.Application.Context;
using SharedKernel.Persistence.Abstractions.Context;

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
/// <see cref="GetResourceHistoryAsync"/>/<see cref="GetActorActionsAsync"/> — like the real
/// <c>EfAuditQueryService</c> — apply tenant scoping THEMSELVES (from <paramref name="tenantContext"/>),
/// never trusting a tenant value the specification might carry (it carries none — see
/// <see cref="AuditResourceHistorySpecification"/>'s remarks), then evaluate the supplied
/// the audit keyset specifications in-memory: filter (<c>Criteria</c>), order
/// (<c>OrderBy</c>/<c>OrderByDescending</c> + <c>ThenBys</c>), seek past the cursor
/// (<c>AfterKey</c>/<c>AfterId</c>/<c>Descending</c>), then take <c>Take + 1</c> and hand the lookahead
/// to <see cref="CursorPagedList{T}.FromLookahead"/> — mirroring
/// <c>Persistence/FakeRepository{TAggregate,TId}.ListKeysetAsync</c>'s established technique.
/// </para>
/// <para>
/// <see cref="VerifyFullChainAsync"/> is a deliberate TEST-DOUBLE SIMPLIFICATION: it re-verifies every
/// record via <see cref="FakeAuditTrailWriter.ComputeHash"/> and its own sequence/link continuity —
/// sufficient to prove the tamper/gap-detection contract in a test that seeds a small, self-contained
/// chain via <see cref="FakeAuditTrailWriter.Seed"/>. <see cref="VerifyChainFromCheckpointAsync"/> is
/// NOT implemented by this fake (checkpoints need real asymmetric signing) — it throws
/// <see cref="NotSupportedException"/>; a test needing checkpoint behavior exercises the real
/// <c>EfAuditCheckpointService</c>/<c>EfAuditQueryService</c> against Postgres instead.
/// </para>
/// </remarks>
public sealed class FakeAuditQueryService : IAuditQueryService
{
    private readonly FakeAuditTrailWriter _writer;
    private readonly IRequestContext _tenantContext;
    private readonly ICrossTenantScope _crossTenantScope;

    /// <summary>Initialises a new <see cref="FakeAuditQueryService"/> reading from <paramref name="writer"/>.</summary>
    /// <param name="writer">The writer whose recorded audit trail this query service reads.</param>
    /// <param name="tenantContext">
    /// Resolves the current tenant for <see cref="GetResourceHistoryAsync"/>/<see cref="GetActorActionsAsync"/>/
    /// <see cref="ExportRangeAsync"/>/<see cref="VerifyFullChainAsync"/>. Defaults to a fresh
    /// <see cref="FakeAuditActorContext"/> when omitted.
    /// </param>
    /// <param name="crossTenantScope">
    /// Gates <see cref="GetResourceHistoryAcrossTenantsAsync"/>. Defaults to a fresh
    /// <see cref="CrossTenantScope"/> when omitted.
    /// </param>
    public FakeAuditQueryService(
        FakeAuditTrailWriter writer,
        IRequestContext? tenantContext = null,
        ICrossTenantScope? crossTenantScope = null)
    {
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        _tenantContext = tenantContext ?? new FakeAuditActorContext();
        _crossTenantScope = crossTenantScope ?? new CrossTenantScope();
    }

    /// <inheritdoc />
    public Task<CursorPagedList<AuditRecord>> GetResourceHistoryAsync(
        AuditResourceHistorySpecification specification,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(specification);

        var tenantId = _tenantContext.TenantId;
        var scoped = _writer.Records.Where(r => r.TenantId == tenantId).ToList();
        var page = EvaluateKeyset(
            scoped, specification, r => r.Sequence, specification.AfterKey, specification.AfterId,
            specification.Descending, specification.PageSize);

        return Task.FromResult(CursorPagedList<AuditRecord>.FromLookahead(
            page, specification.PageSize, last => PageCursor.Encode(last.Sequence, last.Id)));
    }

    /// <inheritdoc />
    public Task<CursorPagedList<AuditRecord>> GetActorActionsAsync(
        AuditActorActionsSpecification specification,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(specification);

        var tenantId = _tenantContext.TenantId;
        var scoped = _writer.Records.Where(r => r.TenantId == tenantId).ToList();
        var page = EvaluateKeyset(
            scoped, specification, r => r.OccurredOn, specification.AfterKey, specification.AfterId,
            specification.Descending, specification.PageSize);

        return Task.FromResult(CursorPagedList<AuditRecord>.FromLookahead(
            page, specification.PageSize, last => PageCursor.Encode(last.OccurredOn, last.Id)));
    }

    /// <inheritdoc />
    public Task<CursorPagedList<AuditRecord>> GetResourceHistoryAcrossTenantsAsync(
        string resourceType,
        string resourceId,
        Guid? afterId,
        bool descending,
        int take,
        CancellationToken cancellationToken = default)
    {
        if (!_crossTenantScope.IsActive)
        {
            throw new InvalidOperationException(
                $"'{nameof(GetResourceHistoryAcrossTenantsAsync)}' bypasses tenant isolation and " +
                $"requires an active '{nameof(ICrossTenantScope)}'.");
        }

        var specification = new AuditCrossTenantResourceHistorySpecification(resourceType, resourceId, afterId, descending, take);
        var page = EvaluateKeyset(_writer.Records, specification, r => r.Id, specification.AfterId, specification.AfterId, specification.Descending, specification.PageSize);

        return Task.FromResult(CursorPagedList<AuditRecord>.FromLookahead(
            page, specification.PageSize, last => PageCursor.Encode(last.Id, last.Id)));
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<AuditRecord> ExportRangeAsync(
        string resourceType,
        DateTimeOffset from,
        DateTimeOffset to,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.TenantId;
        var rows = _writer.Records
            .Where(r => r.TenantId == tenantId && r.ResourceType == resourceType && r.OccurredOn >= from && r.OccurredOn <= to)
            .OrderBy(r => r.Sequence);

        foreach (var record in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return record;
            await Task.Yield();
        }
    }

    /// <inheritdoc />
    public Task<AuditChainVerificationResult> VerifyFullChainAsync(
        string resourceType,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resourceType);

        var tenantId = _tenantContext.TenantId;
        var chain = _writer.Records
            .Where(r => r.TenantId == tenantId && r.ResourceType == resourceType)
            .OrderBy(r => r.Sequence)
            .ToList();

        var expectedSequence = 1L;
        string? previousHash = null;
        var recordsChecked = 0;

        foreach (var record in chain)
        {
            recordsChecked++;

            if (record.Sequence != expectedSequence)
            {
                return Task.FromResult(AuditChainVerificationResult.Broken(
                    expectedSequence, record.Id, $"expected sequence {expectedSequence} but found {record.Sequence}", recordsChecked));
            }

            if (!string.Equals(record.RecordHash, FakeAuditTrailWriter.ComputeHash(record), StringComparison.Ordinal))
            {
                return Task.FromResult(AuditChainVerificationResult.Broken(
                    record.Sequence, record.Id, "the record's stored hash does not match its recomputed hash (tampered)", recordsChecked));
            }

            if (!string.Equals(record.PreviousRecordHash, previousHash, StringComparison.Ordinal))
            {
                return Task.FromResult(AuditChainVerificationResult.Broken(
                    record.Sequence, record.Id, "the record's previous-hash link does not match the prior record actually found (broken link)", recordsChecked));
            }

            previousHash = record.RecordHash;
            expectedSequence++;
        }

        return Task.FromResult(AuditChainVerificationResult.Intact(recordsChecked));
    }

    /// <inheritdoc />
    /// <exception cref="NotSupportedException">Always — see class remarks.</exception>
    public Task<AuditChainVerificationResult> VerifyChainFromCheckpointAsync(
        AuditChainCheckpoint checkpoint,
        AuditChainCheckpoint? expectedHead,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException(
            $"{nameof(FakeAuditQueryService)} does not support checkpoint-anchored verification — " +
            "checkpoints require real asymmetric signing. Test against the real EfAuditQueryService/EfAuditCheckpointService instead.");

    // Mirrors KeysetQueryableExtensions.ToKeysetPage: order by key then id (both in the requested direction),
    // seek past the cursor, and read one row beyond the page for FromLookahead.
    private static IReadOnlyList<AuditRecord> EvaluateKeyset<TKey>(
        IReadOnlyList<AuditRecord> source,
        ISpecification<AuditRecord> spec,
        Func<AuditRecord, TKey> keySelector,
        TKey? afterKey,
        Guid? afterId,
        bool descending,
        int pageSize)
        where TKey : struct, IComparable<TKey>
    {
        IEnumerable<AuditRecord> query = source;

        if (spec.Criteria is not null)
        {
            query = query.Where(spec.Criteria.Compile());
        }

        var ordered = descending
            ? query.OrderByDescending(keySelector).ThenByDescending(r => r.Id)
            : query.OrderBy(keySelector).ThenBy(r => r.Id);

        IEnumerable<AuditRecord> afterCursor = ordered;
        if (afterKey is { } key && afterId is { } id)
        {
            var direction = descending ? -1 : 1;
            afterCursor = ordered.Where(item =>
            {
                var byKey = keySelector(item).CompareTo(key);
                return direction * (byKey != 0 ? byKey : item.Id.CompareTo(id)) > 0;
            });
        }

        return [.. afterCursor.Take(pageSize + 1)];
    }
}

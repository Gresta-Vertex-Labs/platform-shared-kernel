using System.Runtime.CompilerServices;
using SharedKernel.Execution.Context;
using SharedKernel.Contracts.Pagination;
using SharedKernel.Persistence.Abstractions.Context;
using SharedKernel.Persistence.EfCore.Auditing;

namespace SharedKernel.Testing.Persistence;

/// <summary>
/// In-memory fake of <see cref="IAuditQueryService"/> over a <see cref="FakeAuditTrailWriter"/>'s records,
/// with the real service's tenant scoping: the caller's own tenant only, and cross-tenant reads behind an
/// active <see cref="ICrossTenantScope"/>.
/// </summary>
/// <remarks>
/// Verification only checks sequence contiguity and counts erased payloads — there are no MACs in the fake.
/// Checkpoint verification is not supported; test it against the real package.
/// </remarks>
public sealed class FakeAuditQueryService : IAuditQueryService
{
    private readonly FakeAuditTrailWriter _writer;
    private readonly IRequestContext _requestContext;
    private readonly ICrossTenantScope _crossTenantScope;

    /// <summary>Initialises the fake.</summary>
    /// <param name="writer">The writer whose records are queried.</param>
    /// <param name="requestContext">The caller's tenant; defaults to a <see cref="FakeAuditActorContext"/>.</param>
    /// <param name="crossTenantScope">The cross-tenant scope; defaults to one that is never active.</param>
    public FakeAuditQueryService(
        FakeAuditTrailWriter writer,
        IRequestContext? requestContext = null,
        ICrossTenantScope? crossTenantScope = null)
    {
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        _requestContext = requestContext ?? new FakeAuditActorContext();
        _crossTenantScope = crossTenantScope ?? InactiveScope.Instance;
    }

    /// <inheritdoc />
    public Task<CursorPagedList<AuditRecord>> QueryAsync(AuditRecordQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var tenantId = _requestContext.TenantId;
        return Task.FromResult(Page(_writer.Records.Where(r => r.TenantId == tenantId), query));
    }

    /// <inheritdoc />
    public Task<CursorPagedList<AuditRecord>> QueryAcrossTenantsAsync(AuditRecordQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (!_crossTenantScope.IsActive)
            throw new InvalidOperationException($"'{nameof(QueryAcrossTenantsAsync)}' requires an active cross-tenant scope.");

        return Task.FromResult(Page(_writer.Records, query));
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<AuditRecord> ExportRangeAsync(
        string resourceType,
        DateTimeOffset from,
        DateTimeOffset to,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var tenantId = _requestContext.TenantId;
        var rows = _writer.Records
            .Where(r => r.TenantId == tenantId && r.ResourceType == resourceType && r.OccurredOn >= from && r.OccurredOn <= to)
            .OrderBy(r => r.OccurredOn).ThenBy(r => r.Id);

        foreach (var record in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return record;
            await Task.Yield();
        }
    }

    /// <inheritdoc />
    public Task<AuditChainVerificationResult> VerifyChainAsync(string resourceType, bool requirePayloads = false, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resourceType);

        var tenantId = _requestContext.TenantId;
        var chain = _writer.Records
            .Where(r => r.TenantId == tenantId && r.ResourceType == resourceType && r.IsSealed)
            .OrderBy(r => r.Sequence)
            .ToList();

        var expected = 1L;
        var erased = 0L;
        foreach (var record in chain)
        {
            if (record.Sequence != expected)
                return Task.FromResult(Fail(AuditVerificationStatus.Broken, AuditVerificationFailureKind.SequenceGap, expected, record.Id, expected - 1, erased));

            if (record.PayloadErased)
            {
                if (requirePayloads)
                    return Task.FromResult(Fail(AuditVerificationStatus.Unverifiable, AuditVerificationFailureKind.PayloadErased, expected, record.Id, expected - 1, erased));
                erased++;
            }

            expected++;
        }

        return Task.FromResult(new AuditChainVerificationResult
        {
            Status = AuditVerificationStatus.Intact,
            RecordsChecked = chain.Count,
            ErasedPayloads = erased,
            HeadSequence = chain.Count == 0 ? null : chain.Count,
        });
    }

    /// <inheritdoc />
    public Task<AuditChainVerificationResult> VerifyChainFromCheckpointAsync(
        AuditChainCheckpoint checkpoint,
        AuditChainCheckpoint? expectedHead = null,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException(
            $"{nameof(FakeAuditQueryService)} does not support checkpoint verification — checkpoints need real signing. " +
            "Test against SharedKernel.Persistence.EfCore.Auditing on PostgreSQL.");

    /// <inheritdoc />
    public Task<AuditRecordVerificationResult?> VerifyRecordAsync(Guid recordId, bool requirePayload = false, CancellationToken cancellationToken = default)
    {
        var record = _writer.Records.FirstOrDefault(r => r.Id == recordId);
        if (record is null || (record.TenantId != _requestContext.TenantId && !_crossTenantScope.IsActive))
            return Task.FromResult<AuditRecordVerificationResult?>(null);

        AuditRecordVerificationResult result = record.PayloadErased && requirePayload
            ? new() { Status = AuditVerificationStatus.Unverifiable, FailureKind = AuditVerificationFailureKind.PayloadErased, PayloadErased = true }
            : new() { Status = AuditVerificationStatus.Intact, PayloadErased = record.PayloadErased };
        return Task.FromResult<AuditRecordVerificationResult?>(result);
    }

    private static CursorPagedList<AuditRecord> Page(IEnumerable<AuditRecord> source, AuditRecordQuery query)
    {
        if (query.Limit is < 1 or > AuditQueryLimits.MaxPageSize)
            throw new ArgumentOutOfRangeException(nameof(query), query.Limit, $"Limit must be between 1 and {AuditQueryLimits.MaxPageSize}.");

        var rows = source.Where(r =>
            (query.ResourceType is null || r.ResourceType == query.ResourceType) &&
            (query.ResourceId is null || r.ResourceId == query.ResourceId) &&
            (query.ActorId is null || r.ActorId == query.ActorId) &&
            (query.From is null || r.OccurredOn >= query.From) &&
            (query.To is null || r.OccurredOn <= query.To));

        var ordered = query.Descending
            ? rows.OrderByDescending(r => r.OccurredOn).ThenByDescending(r => r.Id)
            : rows.OrderBy(r => r.OccurredOn).ThenBy(r => r.Id);

        IEnumerable<AuditRecord> page = ordered;
        if (query.Cursor is not null)
        {
            var position = PageCursor.Decode<DateTimeOffset, Guid>(query.Cursor);
            if (position.IsFailure)
                throw new ArgumentException("The audit query cursor is invalid.", nameof(query));

            var (key, id) = (position.Value.Key, position.Value.Id);
            page = query.Descending
                ? ordered.Where(r => r.OccurredOn < key || (r.OccurredOn == key && r.Id.CompareTo(id) < 0))
                : ordered.Where(r => r.OccurredOn > key || (r.OccurredOn == key && r.Id.CompareTo(id) > 0));
        }

        return CursorPagedList<AuditRecord>.FromLookahead(
            [.. page.Take(query.Limit + 1)], query.Limit, last => PageCursor.Encode(last.OccurredOn, last.Id));
    }

    private static AuditChainVerificationResult Fail(
        AuditVerificationStatus status, AuditVerificationFailureKind kind, long sequence, Guid recordId, long head, long erased) =>
        new()
        {
            Status = status,
            FailureKind = kind,
            FailedAtSequence = sequence,
            FailedAtRecordId = recordId,
            Reason = kind.ToString(),
            RecordsChecked = head + 1,
            ErasedPayloads = erased,
            HeadSequence = head == 0 ? null : head,
        };

    private sealed class InactiveScope : ICrossTenantScope
    {
        public static readonly InactiveScope Instance = new();

        public bool IsActive => false;

        public IDisposable Enter(string reason) =>
            throw new NotSupportedException(
                "FakeAuditQueryService was created without a cross-tenant scope; pass one (for example a CrossTenantScope) to enter it.");
    }
}

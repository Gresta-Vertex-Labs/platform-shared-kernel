using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SharedKernel.Contracts.Pagination;
using SharedKernel.Cryptography.Signing;
using SharedKernel.Persistence.Abstractions.Auditing;
using SharedKernel.Persistence.Abstractions.Context;
using SharedKernel.Persistence.EfCore.Auditing.Chain;
using SharedKernel.Persistence.EfCore.Auditing.Checkpoints;
using SharedKernel.Persistence.EfCore.Auditing.Diagnostics;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.EfCore.Specifications;

namespace SharedKernel.Persistence.EfCore.Auditing;

/// <summary>
/// EF Core implementation of <see cref="IAuditQueryService"/>.
/// </summary>
/// <remarks>
/// <para>
/// Built directly on <see cref="ISpecificationEvaluator{T}"/> +
/// <see cref="DbContext.Set{TEntity}"/>, NOT <c>IReadRepository&lt;AuditRecord,Guid&gt;</c> — see
/// <see cref="AuditRecord"/>'s remarks for why.
/// </para>
/// <para>
/// <strong>Tenant scoping is enforced here, never trusted from a caller-constructed specification:</strong>
/// <see cref="GetResourceHistoryAsync"/>/<see cref="GetActorActionsAsync"/> pre-filter the source
/// <see cref="IQueryable{T}"/> by <see cref="ICurrentTenantContext.TenantId"/> BEFORE handing it to the
/// evaluator, so <see cref="AuditResourceHistorySpecification"/>/<see cref="AuditActorActionsSpecification"/>
/// — which deliberately carry no tenant parameter — can never be used to read another tenant's chain
/// even if a caller constructed one directly. <see cref="GetResourceHistoryAcrossTenantsAsync"/> is the
/// sole, separately-named, <see cref="ICrossTenantScope"/>-gated exception.
/// </para>
/// </remarks>
public sealed class EfAuditQueryService : IAuditQueryService
{
    private readonly SharedKernelDbContext _dbContext;
    private readonly ISpecificationEvaluator<AuditRecord> _evaluator;
    private readonly ICurrentTenantContext _tenantContext;
    private readonly ICrossTenantScope _crossTenantScope;
    private readonly IAuditChainKeyProvider _keyProvider;
    private readonly IHmacSigner _hmacSigner;
    private readonly IAsymmetricSignatureService? _signatureService;
    private readonly ILogger<EfAuditQueryService> _logger;

    /// <summary>Initialises a new <see cref="EfAuditQueryService"/>.</summary>
    public EfAuditQueryService(
        SharedKernelDbContext dbContext,
        ISpecificationEvaluator<AuditRecord> evaluator,
        ICurrentTenantContext tenantContext,
        ICrossTenantScope crossTenantScope,
        IAuditChainKeyProvider keyProvider,
        IHmacSigner hmacSigner,
        ILogger<EfAuditQueryService> logger,
        IAsymmetricSignatureService? signatureService = null)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(evaluator);
        ArgumentNullException.ThrowIfNull(tenantContext);
        ArgumentNullException.ThrowIfNull(crossTenantScope);
        ArgumentNullException.ThrowIfNull(keyProvider);
        ArgumentNullException.ThrowIfNull(hmacSigner);
        ArgumentNullException.ThrowIfNull(logger);

        _dbContext = dbContext;
        _evaluator = evaluator;
        _tenantContext = tenantContext;
        _crossTenantScope = crossTenantScope;
        _keyProvider = keyProvider;
        _hmacSigner = hmacSigner;
        _logger = logger;
        _signatureService = signatureService;
    }

    /// <inheritdoc />
    public async Task<CursorPagedList<AuditRecord>> GetResourceHistoryAsync(
        AuditResourceHistorySpecification specification,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(specification);

        var tenantId = _tenantContext.TenantId;
        var source = _dbContext.Set<AuditRecord>().Where(r => r.TenantId == tenantId);
        var query = _evaluator.GetKeysetQuery(source, specification);

        var rows = await query.ToListAsync(cancellationToken).ConfigureAwait(false);
        return CursorPagedList<AuditRecord>.FromLookahead(
            rows, specification.Take!.Value, last => PageCursor.Encode(last.Sequence, last.Id));
    }

    /// <inheritdoc />
    public async Task<CursorPagedList<AuditRecord>> GetActorActionsAsync(
        AuditActorActionsSpecification specification,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(specification);

        var tenantId = _tenantContext.TenantId;
        var source = _dbContext.Set<AuditRecord>().Where(r => r.TenantId == tenantId);
        var query = _evaluator.GetKeysetQuery(source, specification);

        var rows = await query.ToListAsync(cancellationToken).ConfigureAwait(false);
        return CursorPagedList<AuditRecord>.FromLookahead(
            rows, specification.Take!.Value, last => PageCursor.Encode(last.OccurredOn, last.Id));
    }

    /// <inheritdoc />
    public async Task<CursorPagedList<AuditRecord>> GetResourceHistoryAcrossTenantsAsync(
        string resourceType,
        string resourceId,
        Guid? afterId,
        bool descending,
        int take,
        CancellationToken cancellationToken = default)
    {
        RequireActiveCrossTenantScope(nameof(GetResourceHistoryAcrossTenantsAsync));

        var specification = new AuditCrossTenantResourceHistorySpecification(
            resourceType, resourceId, afterId, descending, take);

        var query = _evaluator.GetKeysetQuery(_dbContext.Set<AuditRecord>().AsQueryable(), specification);
        var rows = await query.ToListAsync(cancellationToken).ConfigureAwait(false);

        return CursorPagedList<AuditRecord>.FromLookahead(
            rows, specification.Take!.Value, last => PageCursor.Encode(last.Id, last.Id));
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<AuditRecord> ExportRangeAsync(
        Guid? tenantId,
        string resourceType,
        DateTimeOffset from,
        DateTimeOffset to,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceType);

        var query = _dbContext.Set<AuditRecord>()
            .Where(r => r.TenantId == tenantId && r.ResourceType == resourceType && r.OccurredOn >= from && r.OccurredOn <= to)
                .OrderBy(r => r.Sequence)
                    .AsNoTracking()
                        .AsAsyncEnumerable();

        await foreach (var record in query.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            yield return record;
        }
    }

    /// <inheritdoc />
    public async Task<AuditChainVerificationResult> VerifyFullChainAsync(
        Guid? tenantId,
        string resourceType,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceType);

        var chainKey = AuditChainKeyFormat.Build(tenantId, resourceType);
        var records = _dbContext.Set<AuditRecord>()
            .Where(r => r.TenantId == tenantId && r.ResourceType == resourceType)
                .OrderBy(r => r.Sequence)
                    .AsNoTracking()
                        .AsAsyncEnumerable();

        var (result, _) = await VerifyStreamAsync(chainKey, records, expectedStartSequence: 1, expectedPreviousHash: null, cancellationToken)
            .ConfigureAwait(false);
        return result;
    }

    /// <inheritdoc />
    public async Task<AuditChainVerificationResult> VerifyChainFromCheckpointAsync(
        AuditChainCheckpoint checkpoint,
        AuditChainCheckpoint? expectedHead,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);

        if (_signatureService is null)
        {
            throw new InvalidOperationException(
                $"{nameof(VerifyChainFromCheckpointAsync)} requires an {nameof(IAsymmetricSignatureService)} " +
                "to verify the checkpoint's signature — register one (01.Core's AddSharedKernelCryptography().AddAsymmetricSigning()).");
        }

        if (expectedHead is not null &&
            (expectedHead.TenantId != checkpoint.TenantId ||
             !string.Equals(expectedHead.ResourceType, checkpoint.ResourceType, StringComparison.Ordinal)))
        {
            throw new ArgumentException(
                $"{nameof(expectedHead)} names a different chain than {nameof(checkpoint)}.", nameof(expectedHead));
        }

        await VerifyCheckpointSignatureAsync(checkpoint, cancellationToken).ConfigureAwait(false);
        if (expectedHead is not null)
            await VerifyCheckpointSignatureAsync(expectedHead, cancellationToken).ConfigureAwait(false);

        var chainKey = AuditChainKeyFormat.Build(checkpoint.TenantId, checkpoint.ResourceType);

        var anchor = await _dbContext.Set<AuditRecord>()
            .AsNoTracking()
                .SingleOrDefaultAsync(
                r => r.TenantId == checkpoint.TenantId && r.ResourceType == checkpoint.ResourceType && r.Sequence == checkpoint.Sequence,
                cancellationToken)
                    .ConfigureAwait(false);

        if (anchor is null)
        {
            return Broken(chainKey, checkpoint.Sequence, null, "the checkpointed record no longer exists (deleted)", 0);
        }

        if (!string.Equals(anchor.RecordHash, checkpoint.RecordHash, StringComparison.Ordinal))
        {
            return Broken(chainKey, checkpoint.Sequence, anchor.Id, "the checkpointed record's hash no longer matches the checkpoint (tampered)", 1);
        }

        var rest = _dbContext.Set<AuditRecord>()
            .Where(r => r.TenantId == checkpoint.TenantId && r.ResourceType == checkpoint.ResourceType && r.Sequence > checkpoint.Sequence)
                .OrderBy(r => r.Sequence)
                    .AsNoTracking()
                        .AsAsyncEnumerable();

        var (result, lastSequenceSeen) = await VerifyStreamAsync(
            chainKey, rest, expectedStartSequence: checkpoint.Sequence + 1, expectedPreviousHash: checkpoint.RecordHash,
            cancellationToken, recordsAlreadyChecked: 1, initialLastSequenceSeen: checkpoint.Sequence)
                .ConfigureAwait(false);

        if (!result.IsIntact || expectedHead is null)
            return result;

        if (lastSequenceSeen < expectedHead.Sequence)
        {
            // Every record actually found verified correctly, but the chain never reached the
            // sequence an independently-signed, more-recent checkpoint claims it should have —
            // records were deleted from the chain's TAIL after expectedHead was created.
            return Broken(chainKey, expectedHead.Sequence, null, "the chain does not reach the expected head sequence (tail truncated)", result.RecordsChecked);
        }

        return result;
    }

    private async Task<(AuditChainVerificationResult Result, long LastSequenceSeen)> VerifyStreamAsync(
        string chainKey,
        IAsyncEnumerable<AuditRecord> records,
        long expectedStartSequence,
        string? expectedPreviousHash,
        CancellationToken cancellationToken,
        int recordsAlreadyChecked = 0,
        long initialLastSequenceSeen = 0)
    {
        var expectedSequence = expectedStartSequence;
        var previousHash = expectedPreviousHash;
        var recordsChecked = recordsAlreadyChecked;
        var lastSequenceSeen = initialLastSequenceSeen;

        await foreach (var record in records.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            recordsChecked++;
            lastSequenceSeen = record.Sequence;

            if (record.Sequence != expectedSequence)
            {
                return (Broken(chainKey, expectedSequence, record.Id, $"expected sequence {expectedSequence} but found {record.Sequence} (a record was deleted or reordered)", recordsChecked), lastSequenceSeen);
            }

            if (!_keyProvider.TryGetKey(record.KeyId, out var key))
            {
                return (Broken(chainKey, record.Sequence, record.Id, $"key id '{record.KeyId}' is not known to the configured IAuditChainKeyProvider", recordsChecked), lastSequenceSeen);
            }

            var recomputedHash = AuditRecordHasher.ComputeHashHex(_hmacSigner, key.Material, AuditRecordHasher.FromRecord(record));
            if (!string.Equals(recomputedHash, record.RecordHash, StringComparison.Ordinal))
            {
                return (Broken(chainKey, record.Sequence, record.Id, "the record's stored hash does not match its recomputed hash (tampered)", recordsChecked), lastSequenceSeen);
            }

            if (!string.Equals(record.PreviousRecordHash, previousHash, StringComparison.Ordinal))
            {
                return (Broken(chainKey, record.Sequence, record.Id, "the record's previous-hash link does not match the prior record actually found (broken link)", recordsChecked), lastSequenceSeen);
            }

            previousHash = record.RecordHash;
            expectedSequence++;
        }

        return (AuditChainVerificationResult.Intact(recordsChecked), lastSequenceSeen);
    }

    private AuditChainVerificationResult Broken(string chainKey, long sequence, Guid? recordId, string reason, int recordsChecked)
    {
        AuditingLog.ChainVerificationBroken(_logger, chainKey, sequence, reason);
        AuditingMeter.RecordChainVerificationFailure();
        return AuditChainVerificationResult.Broken(sequence, recordId, reason, recordsChecked);
    }

    private async Task VerifyCheckpointSignatureAsync(AuditChainCheckpoint checkpoint, CancellationToken cancellationToken)
    {
        var canonicalBytes = AuditCheckpointCanonicalEncoder.Encode(checkpoint);
        var valid = await _signatureService!
            .VerifyAsync(canonicalBytes, checkpoint.Signature, checkpoint.SigningKeyId, cancellationToken)
                .ConfigureAwait(false);

        if (!valid)
        {
            throw new ArgumentException(
                $"Checkpoint '{checkpoint.Id}' (sequence {checkpoint.Sequence}) failed signature verification " +
                "— it may have been tampered with, or was never genuinely signed by this chain's checkpoint service.");
        }
    }

    private void RequireActiveCrossTenantScope(string methodName)
    {
        if (!_crossTenantScope.IsActive)
        {
            throw new InvalidOperationException(
                $"'{methodName}' bypasses tenant isolation and requires an active " +
                $"'{nameof(ICrossTenantScope)}'. Call 'crossTenantScope.Enter()' (typically " +
                $"'using var _ = crossTenantScope.Enter();') around this call to make the bypass " +
                "explicit and attributable.");
        }
    }
}

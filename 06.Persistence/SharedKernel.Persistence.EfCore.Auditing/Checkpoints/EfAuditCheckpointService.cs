using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SharedKernel.Cryptography.Signing;
using SharedKernel.Persistence.Abstractions.Auditing;
using SharedKernel.Persistence.EfCore.Auditing.Chain;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Primitives.Clocks;

namespace SharedKernel.Persistence.EfCore.Auditing.Checkpoints;

/// <summary>
/// EF Core (read-only) implementation of <see cref="IAuditCheckpointService"/>.
/// </summary>
/// <remarks>
/// Reads the chain head via the ordinary EF query pipeline (no immutability concern — this
/// type never writes an <see cref="AuditRecord"/>) and signs it with <c>01.Core</c>'s
/// <see cref="IAsymmetricSignatureService"/>, keyed by <see cref="AuditChainOptions.CheckpointSigningKeyId"/>.
/// Registered only when <c>.WithAuditChainCheckpoints()</c> is opted into — see that method's remarks.
/// </remarks>
public sealed class EfAuditCheckpointService : IAuditCheckpointService
{
    private readonly SharedKernelDbContext _dbContext;
    private readonly IAsymmetricSignatureService _signatureService;
    private readonly IClock _clock;
    private readonly string _signingKeyId;

    /// <summary>Initialises a new <see cref="EfAuditCheckpointService"/>.</summary>
    public EfAuditCheckpointService(
        SharedKernelDbContext dbContext,
        IAsymmetricSignatureService signatureService,
        IClock clock,
        IOptions<AuditChainOptions> options)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(signatureService);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(options);

        _dbContext = dbContext;
        _signatureService = signatureService;
        _clock = clock;

        _signingKeyId = options.Value.CheckpointSigningKeyId
            ?? throw new InvalidOperationException(
                $"{nameof(AuditChainOptions)}.{nameof(AuditChainOptions.CheckpointSigningKeyId)} is not " +
                "configured. Call '.WithAuditChainCheckpoints(signingKeyId)' with a real key id.");
    }

    /// <inheritdoc />
    public async Task<AuditChainCheckpoint> CreateCheckpointAsync(
        Guid? tenantId,
        string resourceType,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceType);

        var head = await _dbContext.Set<AuditRecord>()
            .Where(r => r.TenantId == tenantId && r.ResourceType == resourceType)
            .OrderByDescending(r => r.Sequence)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (head is null)
        {
            throw new InvalidOperationException(
                $"Cannot checkpoint chain (tenantId: {tenantId}, resourceType: '{resourceType}') — it has no records yet.");
        }

        var id = Guid.CreateVersion7();
        var createdOn = AuditTimestamp.TruncateToMicroseconds(_clock.UtcNow);
        var createdOnMicros = AuditTimestamp.ToUtcMicroseconds(createdOn);

        var canonicalBytes = AuditCheckpointCanonicalEncoder.Encode(
            id, tenantId, resourceType, head.Sequence, head.RecordHash, createdOnMicros, _signingKeyId);

        var signature = await _signatureService
            .SignAsync(canonicalBytes, _signingKeyId, cancellationToken)
            .ConfigureAwait(false);

        return new AuditChainCheckpoint
        {
            Id = id,
            TenantId = tenantId,
            ResourceType = resourceType,
            Sequence = head.Sequence,
            RecordHash = head.RecordHash,
            CreatedOn = createdOn,
            SigningKeyId = _signingKeyId,
            Signature = signature,
        };
    }
}

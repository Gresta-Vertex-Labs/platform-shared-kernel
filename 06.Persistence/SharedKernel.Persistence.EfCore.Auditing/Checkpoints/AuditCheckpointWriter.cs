using System.Data;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedKernel.Cryptography.Signing;
using SharedKernel.Persistence.Abstractions.Connections;
using SharedKernel.Persistence.EfCore.Auditing.Diagnostics;
using SharedKernel.Persistence.EfCore.Auditing.Format;
using SharedKernel.Persistence.EfCore.Auditing.Sealing;
using SharedKernel.Persistence.EfCore.Auditing.Storage;
using SharedKernel.Persistence.EfCore.Auditing.Verification;
using SharedKernel.Primitives.Clocks;

namespace SharedKernel.Persistence.EfCore.Auditing.Checkpoints;

/// <summary>
/// Creates checkpoints (A20): verify incrementally from the latest stored, signature-checked checkpoint to
/// the head first, sign only an intact head, store in the sink. Also checks checkpoint signatures against
/// the pinned accepted key ids.
/// </summary>
internal sealed class AuditCheckpointWriter
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly IAuditCheckpointSink _sink;
    private readonly IAsymmetricSignatureService? _signatureService;
    private readonly IClock _clock;
    private readonly AuditChainVerifier _verifier;
    private readonly ILogger<AuditCheckpointWriter> _logger;
    private readonly string? _signingKeyId;
    private readonly IReadOnlySet<string> _acceptedKeyIds;

    public AuditCheckpointWriter(
        IDbConnectionFactory connectionFactory,
        IAuditRecordAuthenticator authenticator,
        IAuditCheckpointSink sink,
        IClock clock,
        IOptions<AuditLedgerOptions> options,
        ILogger<AuditCheckpointWriter> logger,
        IAsymmetricSignatureService? signatureService = null)
    {
        _connectionFactory = connectionFactory;
        _sink = sink;
        _signatureService = signatureService;
        _clock = clock;
        _logger = logger;
        _verifier = new AuditChainVerifier(connectionFactory, authenticator, logger);
        _signingKeyId = options.Value.CheckpointSigningKeyId;
        _acceptedKeyIds = options.Value.EffectiveAcceptedCheckpointSigningKeyIds();
    }

    /// <summary>Gets whether checkpoints can be created (a signing key id and a signature service are configured).</summary>
    public bool CanSign => _signingKeyId is not null && _signatureService is not null;

    internal AuditChainVerifier Verifier => _verifier;

    /// <summary>Verifies and signs the current head of <paramref name="chain"/>.</summary>
    /// <param name="chain">The chain.</param>
    /// <param name="skipIfUnchanged">Return <see langword="null"/> instead of signing when the latest stored checkpoint already anchors the head.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    public async Task<AuditChainCheckpoint?> CreateAsync(ChainId chain, bool skipIfUnchanged, CancellationToken cancellationToken)
    {
        if (!CanSign)
        {
            throw new InvalidOperationException(
                $"Creating an audit checkpoint needs {nameof(AuditLedgerOptions)}.{nameof(AuditLedgerOptions.CheckpointSigningKeyId)} " +
                $"and a registered {nameof(IAsymmetricSignatureService)} (AddSharedKernelCryptography(...).AddAsymmetricSigning()).");
        }

        using var activity = AuditingMeter.ActivitySource.StartActivity("audit.checkpoint");
        var latest = await _sink.GetLatestAsync(chain.TenantId, chain.ResourceType, cancellationToken).ConfigureAwait(false);

        // A stored checkpoint is only used as a starting point when its own signature is valid under a
        // pinned key; otherwise the chain is verified from genesis.
        ChainAnchor? anchor = latest is not null && await IsAuthenticAsync(latest, cancellationToken).ConfigureAwait(false)
            ? new ChainAnchor(latest.Sequence, latest.HeadMac)
            : null;

        var (verification, headMac) = await _verifier.VerifyCoreAsync(chain, anchor, null, requirePayloads: false, cancellationToken)
            .ConfigureAwait(false);

        if (!verification.IsIntact)
            throw new AuditChainIntegrityException(chain.TenantId, chain.ResourceType, verification);

        if (verification.HeadSequence is not { } headSequence || headMac is null)
        {
            throw new InvalidOperationException(
                $"Audit chain ({chain.TenantLabel}, '{chain.ResourceType}') has no sealed records to checkpoint.");
        }

        if (skipIfUnchanged && anchor is { } a && a.Sequence == headSequence)
            return null;

        var id = Guid.CreateVersion7();
        var createdOn = AuditTimestamp.Truncate(_clock.UtcNow);
        var message = AuditV3Format.EncodeCheckpoint(id, chain.TenantId, chain.ResourceType, headSequence, headMac, createdOn, _signingKeyId!);
        var signature = await _signatureService!.SignAsync(message, _signingKeyId!, cancellationToken).ConfigureAwait(false);

        var checkpoint = new AuditChainCheckpoint
        {
            Id = id,
            TenantId = chain.TenantId,
            ResourceType = chain.ResourceType,
            Sequence = headSequence,
            HeadMac = headMac,
            CreatedOn = createdOn,
            SigningKeyId = _signingKeyId!,
            Signature = signature,
        };

        await _sink.AppendAsync(checkpoint, cancellationToken).ConfigureAwait(false);
        AuditingMeter.RecordCheckpointEmitted();
        AuditingLog.CheckpointEmitted(_logger, chain.TenantLabel, chain.ResourceType, headSequence, _signingKeyId!);
        return checkpoint;
    }

    /// <summary>
    /// Checks <paramref name="checkpoint"/>'s signature, accepting only a pinned key id — never the key the
    /// checkpoint names on its own authority (A20).
    /// </summary>
    public async Task<bool> IsAuthenticAsync(AuditChainCheckpoint checkpoint, CancellationToken cancellationToken)
    {
        if (_signatureService is null || !_acceptedKeyIds.Contains(checkpoint.SigningKeyId))
            return false;

        try
        {
            return await _signatureService
                .VerifyAsync(AuditV3Format.EncodeCheckpoint(checkpoint), checkpoint.Signature, checkpoint.SigningKeyId, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException or ArgumentException or System.Security.Cryptography.CryptographicException)
        {
            return false;
        }
    }

    /// <summary>
    /// Emits a checkpoint for every chain sealed into at or after <paramref name="since"/>, under the sealer
    /// lock (so only one instance emits). Returns -1 when another instance holds the lock.
    /// </summary>
    public async Task<int> EmitChangedAsync(DateTimeOffset since, CancellationToken cancellationToken)
    {
        if (!CanSign)
            return 0;

        var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken).ConfigureAwait(false);
            await using (transaction.ConfigureAwait(false))
            {
                if (!await AuditSealingEngine.TryLockAsync(connection, transaction, cancellationToken).ConfigureAwait(false))
                    return -1;

                var chains = new List<ChainId>();
                await using (var command = LedgerDb.CreateCommand(connection, transaction,
                    $"SELECT DISTINCT tenant_id, resource_type FROM {AuditLedgerSchema.LinksTable} WHERE sealed_on >= @since"))
                {
                    LedgerDb.Add(command, "@since", since.ToUniversalTime(), DbType.DateTimeOffset);
                    await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                    while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                        chains.Add(new ChainId(reader.IsDBNull(0) ? null : reader.GetGuid(0), reader.GetString(1)));
                }

                var emitted = 0;
                foreach (var chain in chains)
                {
                    try
                    {
                        if (await CreateAsync(chain, skipIfUnchanged: true, cancellationToken).ConfigureAwait(false) is not null)
                            emitted++;
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        // A broken chain is already logged by the verifier; nothing is signed for it.
                        AuditingLog.CheckpointEmissionFailed(_logger, ex, chain.TenantLabel, chain.ResourceType);
                        Activity.Current?.SetStatus(ActivityStatusCode.Error);
                    }
                }

                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                return emitted;
            }
        }
    }
}

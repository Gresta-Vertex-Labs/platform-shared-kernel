using System.Data;
using System.Data.Common;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using SharedKernel.Cryptography.Signing;
using SharedKernel.Persistence.Abstractions.Auditing;
using SharedKernel.Persistence.Abstractions.Connections;
using SharedKernel.Persistence.Abstractions.Context;
using SharedKernel.Persistence.Abstractions.Coordination;
using SharedKernel.Persistence.EfCore.Auditing.Chain;
using SharedKernel.Persistence.EfCore.Auditing.Diagnostics;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Primitives.Propagation;

namespace SharedKernel.Persistence.EfCore.Auditing;

/// <summary>
/// EF-Core-package-hosted, but EF-Core-free, implementation of <see cref="IAuditTrailWriter"/> — reads
/// and writes <see cref="AuditRecord"/> rows via raw parameterized ADO.NET, never
/// <see cref="Microsoft.EntityFrameworkCore.DbContext.SaveChangesAsync(System.Threading.CancellationToken)"/>.
/// </summary>
/// <remarks>
/// <para>
/// See <see cref="AuditRecord"/>'s and <see cref="IAuditTrailWriter"/>'s
/// remarks for the design this implements. Raw ADO.NET, not
/// <c>DbContext.SaveChangesAsync</c>, for two independent reasons: (1) the SUCCESS path must enlist in
/// the caller's own ambient connection/transaction — a connection and transaction pair, not a second
/// <c>DbContext</c> instance pointed at the same database — and (2) the read-compute-insert sequence
/// under a per-chain lock is a single, explicit unit of work this type fully controls, never mixed
/// with unrelated tracked-entity changes a caller's own <c>DbContext</c> might also be carrying.
/// </para>
/// <para>
/// <strong>Per-chain serialization:</strong> when <see cref="IAdvisoryTransactionLock"/> is registered
/// (the PostgreSQL package's <c>NpgsqlAdvisoryTransactionLock</c>), every append acquires a
/// transaction-scoped advisory lock keyed by the target chain BEFORE reading the chain head, so
/// concurrent appenders to the SAME chain are fully serialized and always observe a contiguous
/// sequence. When no lock is registered, this writer falls back to unique-constraint retry alone (see
/// <see cref="AuditRecordEntityConfiguration"/>'s <c>chain_key, sequence</c> unique index) — correct,
/// but under heavy concurrent contention on one chain, more retries.
/// </para>
/// </remarks>
public sealed class EfAuditTrailWriter : IAuditTrailWriter
{
    // Higher than a minimal "3 strikes" retry count deliberately: this is the ONLY safety net when
    // no IAdvisoryTransactionLock is registered, and each retry is cheap (a re-read of the chain head
    // plus a re-insert) — a Postgres-backed test with a dozen genuinely concurrent, lock-free writers
    // on the same chain needed more than 5 to reliably avoid exhaustion under real contention.
    private const int MaxSequenceRetries = 10;
    private const int SchemaVersion = 1;
    private const string PostgresUniqueViolationSqlState = "23505";
    private const string SavepointName = "sk_audit_append_attempt";

    private readonly IDbConnectionFactory _connectionFactory;
    private readonly IAmbientDbTransaction _ambientTransaction;
    private readonly IAdvisoryTransactionLock? _advisoryLock;
    private readonly ICurrentActorContext _actorContext;
    private readonly ICurrentTenantContext _tenantContext;
    private readonly IClock _clock;
    private readonly IHmacSigner _hmacSigner;
    private readonly IAuditChainKeyProvider _keyProvider;
    private readonly ILogger<EfAuditTrailWriter> _logger;

    /// <summary>Initialises a new <see cref="EfAuditTrailWriter"/>.</summary>
    public EfAuditTrailWriter(
        IDbConnectionFactory connectionFactory,
        IAmbientDbTransaction ambientTransaction,
        ICurrentActorContext actorContext,
        ICurrentTenantContext tenantContext,
        IClock clock,
        IHmacSigner hmacSigner,
        IAuditChainKeyProvider keyProvider,
        ILogger<EfAuditTrailWriter> logger,
        IAdvisoryTransactionLock? advisoryLock = null)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);
        ArgumentNullException.ThrowIfNull(ambientTransaction);
        ArgumentNullException.ThrowIfNull(actorContext);
        ArgumentNullException.ThrowIfNull(tenantContext);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(hmacSigner);
        ArgumentNullException.ThrowIfNull(keyProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _connectionFactory = connectionFactory;
        _ambientTransaction = ambientTransaction;
        _actorContext = actorContext;
        _tenantContext = tenantContext;
        _clock = clock;
        _hmacSigner = hmacSigner;
        _keyProvider = keyProvider;
        _logger = logger;
        _advisoryLock = advisoryLock;

        if (_advisoryLock is null)
            AuditingLog.NoAdvisoryTransactionLockRegistered(_logger);
    }

    /// <inheritdoc />
    public async Task<AuditRecord> RecordAsync(AuditEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var startedAt = Stopwatch.GetTimestamp();
        var tenantId = _tenantContext.TenantId;
        var chainKey = AuditChainKeyFormat.Build(tenantId, entry.ResourceType);
        var actorId = _actorContext.ActorId;
        var actorKind = _actorContext.ActorKind;
        var occurredOn = AuditTimestamp.TruncateToMicroseconds(_clock.UtcNow);
        var occurredOnMicros = AuditTimestamp.ToUtcMicroseconds(occurredOn);
        var correlationId = Activity.Current?.GetBaggageItem(WellKnownBaggageKeys.CorrelationId);
        var key = _keyProvider.GetCurrentKey();

        // Transaction semantics — see IAuditTrailWriter's remarks for the exact rule: a SUCCEEDED
        // entry enlists in the caller's own ambient transaction (commits/rolls back atomically with
        // it); a FAILED entry — or a SUCCEEDED one with no ambient transaction active — always gets
        // its own connection and its own transaction, committed here, independent of anything else.
        var ambient = entry.Outcome == AuditOutcome.Succeeded ? _ambientTransaction.Current : null;
        var ownsConnection = ambient is null;

        DbConnection connection;
        DbTransaction? transaction;

        if (ambient is { } current)
        {
            (connection, transaction) = current;
        }
        else
        {
            connection = await _connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
            transaction = null;
        }

        try
        {
            if (ownsConnection)
                transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            if (_advisoryLock is not null)
            {
                await _advisoryLock
                    .AcquireAsync(connection, transaction!, $"sk-audit-chain:{chainKey}", cancellationToken)
                        .ConfigureAwait(false);
            }

            if (entry.IdempotencyKey is { } key1)
            {
                var existing = await TryReadByIdempotencyKeyAsync(connection, transaction, chainKey, key1, cancellationToken)
                    .ConfigureAwait(false);
                if (existing is not null)
                {
                    if (ownsConnection)
                        await transaction!.CommitAsync(cancellationToken).ConfigureAwait(false);

                    AuditingLog.IdempotentDuplicateReturned(_logger, chainKey, key1);
                    AuditingMeter.RecordIdempotentDuplicate();
                    return existing;
                }
            }

            // PostgreSQL aborts the ENTIRE transaction on a constraint violation — every subsequent
            // statement fails with 25P02 ("current transaction is aborted") until a ROLLBACK. A
            // savepoint per attempt is what makes retrying an INSERT within the SAME transaction
            // (ambient or owned) actually work, rather than merely compiling: caught by the
            // Postgres-backed "no advisory lock, retry-on-conflict" test, not by inspection.
            var canUseSavepoints = transaction is not null && transaction.SupportsSavepoints;

            for (var attempt = 1; attempt <= MaxSequenceRetries; attempt++)
            {
                if (canUseSavepoints)
                    await transaction!.SaveAsync(SavepointName, cancellationToken).ConfigureAwait(false);

                var (headSequence, headHash) = await ReadChainHeadAsync(connection, transaction, chainKey, cancellationToken)
                    .ConfigureAwait(false);
                var sequence = headSequence + 1;
                var id = Guid.CreateVersion7();

                var fields = new AuditRecordHasher.Fields(
                    id, tenantId, actorId, actorKind, entry.Action, entry.ResourceType, entry.ResourceId,
                    sequence, occurredOnMicros, entry.BeforeSnapshot, entry.AfterSnapshot, correlationId,
                    entry.ApprovalId, entry.Outcome, entry.ErrorCode, entry.ClientId, entry.SessionId,
                    entry.ImpersonatorId, entry.SourceService, entry.IdempotencyKey, headHash, SchemaVersion);

                var recordHash = AuditRecordHasher.ComputeHashHex(_hmacSigner, key.Material, in fields);

                var record = new AuditRecord
                {
                    Id = id,
                    TenantId = tenantId,
                    ActorId = actorId,
                    ActorKind = actorKind,
                    Action = entry.Action,
                    ResourceType = entry.ResourceType,
                    ResourceId = entry.ResourceId,
                    Sequence = sequence,
                    OccurredOn = occurredOn,
                    BeforeSnapshot = entry.BeforeSnapshot,
                    AfterSnapshot = entry.AfterSnapshot,
                    CorrelationId = correlationId,
                    ApprovalId = entry.ApprovalId,
                    Outcome = entry.Outcome,
                    ErrorCode = entry.ErrorCode,
                    ClientId = entry.ClientId,
                    SessionId = entry.SessionId,
                    ImpersonatorId = entry.ImpersonatorId,
                    SourceService = entry.SourceService,
                    IdempotencyKey = entry.IdempotencyKey,
                    HashAlgorithm = AuditHashAlgorithmNames.HmacSha256,
                    SchemaVersion = SchemaVersion,
                    KeyId = key.Id,
                    RecordHash = recordHash,
                    PreviousRecordHash = headHash,
                };

                try
                {
                    await InsertAsync(connection, transaction, record, cancellationToken).ConfigureAwait(false);

                    if (ownsConnection)
                        await transaction!.CommitAsync(cancellationToken).ConfigureAwait(false);

                    AuditingMeter.RecordAppendDuration(Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
                    return record;
                }
                catch (DbException ex) when (IsUniqueConstraintViolation(ex))
                {
                    // Undo just this attempt's aborted INSERT, not the whole transaction — any
                    // ambient business writes staged earlier in the SAME transaction (the
                    // Outcome=Succeeded/ambient-transaction path) must survive a sequence-conflict
                    // retry untouched.
                    if (canUseSavepoints)
                        await transaction!.RollbackAsync(SavepointName, cancellationToken).ConfigureAwait(false);

                    if (entry.IdempotencyKey is { } key2)
                    {
                        var existing = await TryReadByIdempotencyKeyAsync(connection, transaction, chainKey, key2, cancellationToken)
                            .ConfigureAwait(false);
                        if (existing is not null)
                        {
                            if (ownsConnection)
                                await transaction!.CommitAsync(cancellationToken).ConfigureAwait(false);

                            AuditingLog.IdempotentDuplicateReturned(_logger, chainKey, key2);
                            AuditingMeter.RecordIdempotentDuplicate();
                            return existing;
                        }
                    }

                    if (attempt >= MaxSequenceRetries)
                    {
                        AuditingLog.SequenceConflictExhausted(_logger, chainKey, MaxSequenceRetries);
                        throw new InvalidOperationException(
                            $"Failed to append an audit record to chain '{chainKey}' after " +
                            $"{MaxSequenceRetries} attempts due to repeated sequence conflicts.", ex);
                    }

                    AuditingLog.SequenceConflictRetried(_logger, chainKey, attempt, MaxSequenceRetries);
                    AuditingMeter.RecordSequenceConflictRetry();

                    // Jittered backoff, scaled by attempt number — without it, several writers that
                    // lost the SAME race retry in lockstep and simply collide again immediately (a
                    // real "thundering herd" effect observed against a real Postgres instance with a
                    // dozen genuinely concurrent, lock-free writers on one chain, not a theoretical
                    // concern). Only matters for the no-IAdvisoryTransactionLock fallback path — with
                    // the lock registered, appends are already fully serialized and never conflict.
                    var backoff = TimeSpan.FromMilliseconds(Random.Shared.Next(5, 20) * attempt);
                    await Task.Delay(backoff, cancellationToken).ConfigureAwait(false);
                }
            }

            // Unreachable — the loop above always either returns or throws on its final attempt.
            throw new UnreachableException();
        }
        catch
        {
            if (ownsConnection && transaction is not null)
                await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        finally
        {
            if (ownsConnection)
                await connection.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static bool IsUniqueConstraintViolation(DbException exception) =>
        string.Equals(exception.SqlState, PostgresUniqueViolationSqlState, StringComparison.Ordinal);

    private static async Task<(long Sequence, string? RecordHash)> ReadChainHeadAsync(
        DbConnection connection,
        DbTransaction? transaction,
        string chainKey,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            SELECT "{AuditSchema.Sequence}", "{AuditSchema.RecordHash}"
            FROM "{AuditSchema.TableName}"
            WHERE "{AuditSchema.ChainKey}" = @chainKey
            ORDER BY "{AuditSchema.Sequence}" DESC
            LIMIT 1
            """;
        AddParameter(command, "@chainKey", chainKey);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            return (0L, null);

        var sequence = reader.GetInt64(0);
        var hash = reader.IsDBNull(1) ? null : reader.GetString(1);
        return (sequence, hash);
    }

    private static async Task<AuditRecord?> TryReadByIdempotencyKeyAsync(
        DbConnection connection,
        DbTransaction? transaction,
        string chainKey,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = AuditRecordSql.SelectColumns +
            $"""
            FROM "{AuditSchema.TableName}"
            WHERE "{AuditSchema.ChainKey}" = @chainKey AND "{AuditSchema.IdempotencyKey}" = @idempotencyKey
            LIMIT 1
            """;
        AddParameter(command, "@chainKey", chainKey);
        AddParameter(command, "@idempotencyKey", idempotencyKey);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? AuditRecordSql.Map(reader)
            : null;
    }

    private static async Task InsertAsync(
        DbConnection connection,
        DbTransaction? transaction,
        AuditRecord record,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"""
            INSERT INTO "{AuditSchema.TableName}" (
                "{AuditSchema.Id}", "{AuditSchema.TenantId}", "{AuditSchema.ActorId}", "{AuditSchema.ActorKind}",
                "{AuditSchema.Action}", "{AuditSchema.ResourceType}", "{AuditSchema.ResourceId}", "{AuditSchema.Sequence}",
                "{AuditSchema.OccurredOn}", "{AuditSchema.BeforeSnapshot}", "{AuditSchema.AfterSnapshot}",
                "{AuditSchema.CorrelationId}", "{AuditSchema.ApprovalId}", "{AuditSchema.Outcome}", "{AuditSchema.ErrorCode}",
                "{AuditSchema.ClientId}", "{AuditSchema.SessionId}", "{AuditSchema.ImpersonatorId}", "{AuditSchema.SourceService}",
                "{AuditSchema.IdempotencyKey}", "{AuditSchema.HashAlgorithm}", "{AuditSchema.SchemaVersion}", "{AuditSchema.KeyId}",
                "{AuditSchema.RecordHash}", "{AuditSchema.PreviousRecordHash}"
            ) VALUES (
                @id, @tenantId, @actorId, @actorKind, @action, @resourceType, @resourceId, @sequence, @occurredOn,
                @beforeSnapshot, @afterSnapshot, @correlationId, @approvalId, @outcome, @errorCode, @clientId,
                @sessionId, @impersonatorId, @sourceService, @idempotencyKey, @hashAlgorithm, @schemaVersion,
                @keyId, @recordHash, @previousRecordHash
            )
            """;

        AddParameter(command, "@id", record.Id);
        AddParameter(command, "@tenantId", record.TenantId);
        AddParameter(command, "@actorId", record.ActorId);
        AddParameter(command, "@actorKind", (int)record.ActorKind);
        AddParameter(command, "@action", record.Action);
        AddParameter(command, "@resourceType", record.ResourceType);
        AddParameter(command, "@resourceId", record.ResourceId);
        AddParameter(command, "@sequence", record.Sequence);
        AddParameter(command, "@occurredOn", record.OccurredOn);
        AddParameter(command, "@beforeSnapshot", record.BeforeSnapshot);
        AddParameter(command, "@afterSnapshot", record.AfterSnapshot);
        AddParameter(command, "@correlationId", record.CorrelationId);
        AddParameter(command, "@approvalId", record.ApprovalId);
        AddParameter(command, "@outcome", (int)record.Outcome);
        AddParameter(command, "@errorCode", record.ErrorCode);
        AddParameter(command, "@clientId", record.ClientId);
        AddParameter(command, "@sessionId", record.SessionId);
        AddParameter(command, "@impersonatorId", record.ImpersonatorId);
        AddParameter(command, "@sourceService", record.SourceService);
        AddParameter(command, "@idempotencyKey", record.IdempotencyKey);
        AddParameter(command, "@hashAlgorithm", record.HashAlgorithm);
        AddParameter(command, "@schemaVersion", record.SchemaVersion);
        AddParameter(command, "@keyId", record.KeyId);
        AddParameter(command, "@recordHash", record.RecordHash);
        AddParameter(command, "@previousRecordHash", record.PreviousRecordHash);

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    internal static void AddParameter(DbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }
}

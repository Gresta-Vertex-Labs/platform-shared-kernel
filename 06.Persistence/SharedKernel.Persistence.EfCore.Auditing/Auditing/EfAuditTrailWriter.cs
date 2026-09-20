using System.Data;
using System.Data.Common;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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
/// <para>
/// <strong>The unique-constraint-retry fallback assumes READ COMMITTED isolation</strong> (Postgres's
/// own default, and what a plain <c>ITransactionalUnitOfWork.BeginTransactionAsync()</c> call without an
/// explicit <see cref="System.Data.IsolationLevel"/> uses). Each retry re-reads the chain head with a
/// fresh statement-level snapshot, which is what lets a later attempt observe a DIFFERENT (newer) head
/// than an earlier one on the SAME connection. Under REPEATABLE READ or SERIALIZABLE, a transaction sees
/// one consistent snapshot for its entire duration — every retry inside the SAME ambient transaction
/// would keep re-reading the SAME (already-stale) head and keep colliding on the SAME candidate
/// sequence, exhausting <see cref="MaxSequenceRetries"/> under contention that READ COMMITTED would have
/// resolved. This only matters for a <c>Outcome.Succeeded</c> write enlisted in an ambient transaction
/// explicitly opened at a stricter isolation level AND contending with a concurrent appender to the SAME
/// chain — the <c>Outcome.Failed</c>/no-ambient-transaction path always opens its own fresh transaction
/// and is unaffected.
/// </para>
/// </remarks>
public sealed class EfAuditTrailWriter : IAuditTrailWriter
{
    // Higher than a minimal "3 strikes" retry count deliberately: this is the ONLY safety net when
    // no IAdvisoryTransactionLock is registered, and each retry is cheap (a re-read of the chain head
    // plus a re-insert) — a Postgres-backed test with a dozen genuinely concurrent, lock-free writers
    // on the same chain needed more than 5 to reliably avoid exhaustion under real contention.
    private const int MaxSequenceRetries = 10;

    // v2: HashAlgorithm/KeyId joined the hashed payload (see AuditRecordHasher's remarks) — a hash-
    // format break, so this bumped from 1 alongside AuditRecordHasher's own domain-separator version.
    private const int SchemaVersion = 2;
    private const string PostgresUniqueViolationSqlState = "23505";
    private const string PostgresLockNotAvailableSqlState = "55P03";
    private const string SavepointName = "sk_audit_append_attempt";

    private readonly IDbConnectionFactory _connectionFactory;
    private readonly IAmbientDbTransaction _ambientTransaction;
    private readonly IAdvisoryTransactionLock? _advisoryLock;
    private readonly ICurrentActorContext _actorContext;
    private readonly ICurrentTenantContext _tenantContext;
    private readonly IClock _clock;
    private readonly IHmacSigner _hmacSigner;
    private readonly IAuditChainKeyProvider _keyProvider;
    private readonly TimeSpan _advisoryLockTimeout;
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
        IOptions<AuditChainOptions> options,
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
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _connectionFactory = connectionFactory;
        _ambientTransaction = ambientTransaction;
        _actorContext = actorContext;
        _tenantContext = tenantContext;
        _clock = clock;
        _hmacSigner = hmacSigner;
        _keyProvider = keyProvider;
        _advisoryLockTimeout = options.Value.AdvisoryLockTimeout;
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
        // entry enlists in the caller's own ambient transaction, and ONLY the caller's own ambient
        // transaction — there is no standalone fallback. Recording a "succeeded" attestation with no
        // transactional tie to the business write it describes would be worse than no attestation at
        // all (it could commit before, and regardless of, a business write that never actually
        // happens), so this is a hard, fail-loud requirement, not a graceful degradation.
        var ambientAtEntry = _ambientTransaction.Current;

        if (entry.Outcome == AuditOutcome.Succeeded && ambientAtEntry is null)
        {
            throw new InvalidOperationException(
                $"{nameof(RecordAsync)} was called with {nameof(AuditEntry.Outcome)}=" +
                $"{nameof(AuditOutcome.Succeeded)}, but no ambient database transaction is active " +
                $"({nameof(IAmbientDbTransaction)}.{nameof(IAmbientDbTransaction.Current)} is null). A " +
                $"{nameof(AuditOutcome.Succeeded)}-outcome audit record is only ever written INSIDE the " +
                "SAME transaction as the business write it attests to, so it can commit or roll back " +
                "atomically together with it. Call this from inside an active ITransactionalUnitOfWork " +
                "transaction (BeginTransactionAsync/ExecuteInTransactionAsync), or record " +
                $"{nameof(AuditOutcome.Failed)} if there is no business write for this entry to be " +
                "atomic with.");
        }

        // A FAILED entry — the only remaining case — always gets its own connection and its own
        // transaction, committed here, independent of anything else (including any ambient
        // transaction that may also happen to be open).
        var ambient = entry.Outcome == AuditOutcome.Succeeded ? ambientAtEntry : null;
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
            {
                transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

                // Bounds every lock wait on THIS writer's own, independent connection/transaction —
                // in particular the advisory-lock acquisition below — so a specific, real self-deadlock
                // (a Succeeded entry holding this chain's advisory lock for its ambient transaction's
                // whole lifetime, while a Failed entry on the SAME chain from the SAME logical request
                // blocks trying to acquire it) fails fast instead of hanging forever. Never applied to
                // an ambient (enlisted) transaction — see AuditChainOptions.AdvisoryLockTimeout's remarks.
                await SetLockTimeoutAsync(connection, transaction, _advisoryLockTimeout, cancellationToken).ConfigureAwait(false);
            }

            if (_advisoryLock is not null)
            {
                await _advisoryLock
                    .AcquireAsync(connection, transaction!, $"sk-audit-chain:{chainKey}", cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
            }

            if (entry.IdempotencyKey is { } key1)
            {
                var existing = await TryReadByIdempotencyKeyAsync(connection, transaction, chainKey, key1, cancellationToken)
                    .ConfigureAwait(false);
                if (existing is not null)
                {
                    EnsureIdempotencyFingerprintMatches(entry, existing, chainKey, key1);

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
                    entry.ImpersonatorId, entry.SourceService, entry.IdempotencyKey, headHash,
                    AuditHashAlgorithmNames.HmacSha256, key.Id, SchemaVersion);

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

                    // Releases (never leaves dangling) this attempt's own subtransaction before
                    // committing — a savepoint created but never released/rolled-back would otherwise
                    // accumulate across every RecordAsync call sharing one long-lived ambient
                    // transaction, eventually overflowing Postgres's per-backend cached-subxid limit.
                    if (canUseSavepoints)
                        await transaction!.ReleaseAsync(SavepointName, cancellationToken).ConfigureAwait(false);

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
                    // retry untouched. RELEASE immediately after — otherwise the rolled-back-to
                    // savepoint stays defined (an un-released subtransaction) for the rest of this
                    // ambient transaction's life, and the NEXT attempt's SAVEPOINT (same name) would
                    // nest another one on top of it rather than replacing it.
                    if (canUseSavepoints)
                    {
                        await transaction!.RollbackAsync(SavepointName, cancellationToken).ConfigureAwait(false);
                        await transaction!.ReleaseAsync(SavepointName, cancellationToken).ConfigureAwait(false);
                    }

                    if (entry.IdempotencyKey is { } key2)
                    {
                        var existing = await TryReadByIdempotencyKeyAsync(connection, transaction, chainKey, key2, cancellationToken)
                            .ConfigureAwait(false);
                        if (existing is not null)
                        {
                            EnsureIdempotencyFingerprintMatches(entry, existing, chainKey, key2);

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
        catch (Exception ex)
        {
            if (ownsConnection && ex is DbException dbEx && IsLockTimeout(dbEx))
            {
                // The specific, real self-deadlock this writer can otherwise hit — see
                // AuditChainOptions.AdvisoryLockTimeout's remarks. Logged distinctly from the generic
                // "failed to record a failure" case below: this is a coordination/contention failure,
                // not a data-layer one.
                AuditingLog.AdvisoryLockTimedOut(_logger, chainKey, _advisoryLockTimeout);
            }
            else if (entry.Outcome == AuditOutcome.Failed)
            {
                // Recording a FAILURE itself failed — the compliance-relevant "why did this command
                // fail" information may now be lost entirely, distinct from (and independent of)
                // whatever happens to the business transaction that triggered it.
                AuditingLog.FailureAuditWriteFailed(_logger, ex, chainKey);
            }

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

    private static bool IsLockTimeout(DbException exception) =>
        string.Equals(exception.SqlState, PostgresLockNotAvailableSqlState, StringComparison.Ordinal);

    /// <summary>
    /// Sets <c>lock_timeout</c> for the remainder of <paramref name="transaction"/> ONLY (<c>SET
    /// LOCAL</c> auto-reverts at commit/rollback) — never the session/pool default, and never applied
    /// to an ambient (enlisted) transaction this writer does not own. See
    /// <see cref="AuditChainOptions.AdvisoryLockTimeout"/>'s remarks for why.
    /// </summary>
    private static async Task SetLockTimeoutAsync(
        DbConnection connection,
        DbTransaction transaction,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        // TimeSpan.Zero means "no timeout" (Postgres's own lock_timeout=0 semantics) — the
        // pre-fix, unbounded-wait behavior; an explicit opt-out, so nothing to set.
        if (timeout <= TimeSpan.Zero)
            return;

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        // A trusted, internally-computed integer (bound by AuditChainOptionsValidator to be
        // non-negative) — never caller/user input — and Postgres's SET command does not accept a bind
        // parameter in the value position, so this interpolation is safe.
        command.CommandText = $"SET LOCAL lock_timeout = '{(int)timeout.TotalMilliseconds}ms'";
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Rejects a reused <see cref="AuditEntry.IdempotencyKey"/> whose entry describes a DIFFERENT
    /// logical event than the record already persisted under that key — see
    /// <see cref="IAuditTrailWriter.RecordAsync"/>'s remarks.
    /// </summary>
    private static void EnsureIdempotencyFingerprintMatches(
        AuditEntry entry,
        AuditRecord existing,
        string chainKey,
        string idempotencyKey)
    {
        if (string.Equals(existing.Action, entry.Action, StringComparison.Ordinal) &&
            string.Equals(existing.ResourceType, entry.ResourceType, StringComparison.Ordinal) &&
            string.Equals(existing.ResourceId, entry.ResourceId, StringComparison.Ordinal) &&
            existing.Outcome == entry.Outcome)
        {
            return;
        }

        throw new InvalidOperationException(
            $"Idempotency key '{idempotencyKey}' on chain '{chainKey}' was already used to record a " +
            $"DIFFERENT audit event (Action='{existing.Action}', ResourceType='{existing.ResourceType}', " +
            $"ResourceId='{existing.ResourceId}', Outcome={existing.Outcome}) than the one now being " +
            $"recorded (Action='{entry.Action}', ResourceType='{entry.ResourceType}', " +
            $"ResourceId='{entry.ResourceId}', Outcome={entry.Outcome}). An idempotency key must " +
            "uniquely identify exactly one logical audit event — reusing it for a different event is " +
            "rejected rather than silently discarding the new one.");
    }

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

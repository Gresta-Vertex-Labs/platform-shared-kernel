using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedKernel.Application.Auditing;
using SharedKernel.Application.Context;
using SharedKernel.Persistence.Abstractions.Connections;
using SharedKernel.Persistence.Abstractions.Context;
using SharedKernel.Persistence.Abstractions.Coordination;
using SharedKernel.Persistence.EfCore.Auditing.Diagnostics;
using SharedKernel.Persistence.EfCore.Auditing.Storage;
using SharedKernel.Persistence.EfCore.Auditing.Writing;
using SharedKernel.Persistence.EfCore.Options;
using SharedKernel.Primitives.Clocks;

namespace SharedKernel.Persistence.EfCore.Auditing;

/// <summary>
/// The request-path <see cref="IAuditTrailWriter"/>: one plain <c>INSERT</c> of the record and its
/// payload. No sequence, no hash, no lock, no retry — the background sealer chains the record later.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Transactions.</strong> A <see cref="AuditOutcome.Succeeded"/> entry is written inside the
/// caller's ambient transaction (<see cref="IAmbientDbTransaction"/>) so it commits or rolls back with the
/// business write it attests to; with no ambient transaction it throws. A
/// <see cref="AuditOutcome.Failed"/> entry is written on its own connection in its own single-statement
/// transaction, so it persists even though the business transaction rolled back, and it waits on no lock
/// held by any other transaction.
/// </para>
/// <para>
/// <strong>Never breaks the business transaction.</strong> Field lengths are checked before any SQL is sent
/// (<see cref="AuditFieldLimits"/>), and a reused idempotency key is resolved with
/// <c>ON CONFLICT DO NOTHING</c>, so no audit-side database error aborts the caller's transaction.
/// </para>
/// <para>
/// <strong>Identity</strong> comes from <see cref="IRequestContext"/> (user, actor kind, client, session,
/// impersonator, tenant) and <c>PersistenceServiceOptions.ServiceName</c>; the W3C trace id and correlation
/// id come from the ambient <see cref="Activity"/>. A record without a tenant is only accepted from an
/// authenticated system identity or inside an active cross-tenant scope (A16).
/// </para>
/// </remarks>
public sealed class EfAuditTrailWriter : IAuditTrailWriter
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly IAmbientDbTransaction _ambientTransaction;
    private readonly AuditRecordFactory _factory;
    private readonly ILogger<EfAuditTrailWriter> _logger;

    /// <summary>Initialises a new <see cref="EfAuditTrailWriter"/>.</summary>
    /// <param name="connectionFactory">Opens the connection for failed-outcome entries.</param>
    /// <param name="ambientTransaction">The caller's current transaction, for succeeded-outcome entries.</param>
    /// <param name="requestContext">The caller's identity and tenant.</param>
    /// <param name="crossTenantScope">Whether an explicit cross-tenant scope is active.</param>
    /// <param name="clock">The time source.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="serviceOptions">The service name recorded as source service and as actor of anonymous work.</param>
    public EfAuditTrailWriter(
        IDbConnectionFactory connectionFactory,
        IAmbientDbTransaction ambientTransaction,
        IRequestContext requestContext,
        ICrossTenantScope crossTenantScope,
        IClock clock,
        ILogger<EfAuditTrailWriter> logger,
        IOptions<PersistenceServiceOptions>? serviceOptions = null)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);
        ArgumentNullException.ThrowIfNull(ambientTransaction);
        ArgumentNullException.ThrowIfNull(requestContext);
        ArgumentNullException.ThrowIfNull(crossTenantScope);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);

        _connectionFactory = connectionFactory;
        _ambientTransaction = ambientTransaction;
        _logger = logger;
        _factory = new AuditRecordFactory(
            new AuditCallerScope(requestContext, crossTenantScope),
            clock,
            serviceOptions?.Value.ServiceName ?? new PersistenceServiceOptions().ServiceName);
    }

    /// <inheritdoc />
    Task IAuditTrailWriter.RecordAsync(AuditEntry entry, CancellationToken cancellationToken) =>
        RecordAsync(entry, cancellationToken);

    /// <summary>
    /// Writes <paramref name="entry"/> and returns the stored (not yet sealed) record. A reused
    /// <see cref="AuditEntry.IdempotencyKey"/> returns the record already stored for the same event, and
    /// throws <see cref="InvalidOperationException"/> when that record describes a different event.
    /// </summary>
    /// <param name="entry">The entry to record.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The stored record.</returns>
    /// <exception cref="ArgumentException">A field is missing or exceeds its <see cref="AuditFieldLimits"/> limit.</exception>
    /// <exception cref="InvalidOperationException">
    /// A succeeded entry has no ambient transaction, the caller has no tenant outside an explicit system
    /// scope, or the idempotency key was used for a different event.
    /// </exception>
    public async Task<AuditRecord> RecordAsync(AuditEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var startedAt = Stopwatch.GetTimestamp();
        var tenantId = _factory.Scope.ResolveCallerTenant("Recording an audit entry");
        var pending = _factory.Create(entry, tenantId);

        if (entry.Outcome == AuditOutcome.Succeeded)
        {
            if (_ambientTransaction.Current is not { } ambient)
            {
                throw new InvalidOperationException(
                    $"An audit entry with {nameof(AuditEntry.Outcome)}={nameof(AuditOutcome.Succeeded)} must be written inside the " +
                    "transaction of the business write it attests to, but no ambient database transaction is active. Record it " +
                    "from IUnitOfWork.ExecuteInTransactionAsync or IUnitOfWork.OnBeforeCommit (the AuditingBehavior does this), " +
                    $"or record {nameof(AuditOutcome.Failed)} when there is no business write.");
            }

            var record = await AppendAsync(ambient.Connection, ambient.Transaction, pending, cancellationToken).ConfigureAwait(false);
            AuditingMeter.RecordAppendDuration(Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
            return record;
        }

        try
        {
            var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using (connection.ConfigureAwait(false))
            {
                // One statement, autocommitted: its own transaction, no lock taken on any chain.
                var record = await AppendAsync(connection, null, pending, cancellationToken).ConfigureAwait(false);
                AuditingMeter.RecordAppendDuration(Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
                return record;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not InvalidOperationException)
        {
            AuditingLog.FailureAuditWriteFailed(_logger, ex, entry.ResourceType);
            throw;
        }
    }

    private async Task<AuditRecord> AppendAsync(
        System.Data.Common.DbConnection connection,
        System.Data.Common.DbTransaction? transaction,
        PendingLedgerRecord pending,
        CancellationToken cancellationToken)
    {
        if (await LedgerDb.InsertAsync(connection, transaction, pending, cancellationToken).ConfigureAwait(false))
            return ToRecord(pending);

        // Idempotency conflict: the key is already stored in this chain.
        var fields = pending.Fields;
        var chain = new ChainId(fields.TenantId, fields.ResourceType);
        var existing = await LedgerDb.ReadByIdempotencyKeyAsync(connection, transaction, chain, fields.IdempotencyKey!, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                $"Idempotency key '{fields.IdempotencyKey}' conflicted, but no record holds it; retry the operation.");

        if (!string.Equals(existing.Action, fields.Action, StringComparison.Ordinal) ||
            !string.Equals(existing.ResourceId, fields.ResourceId, StringComparison.Ordinal) ||
            existing.Outcome != fields.Outcome)
        {
            throw new InvalidOperationException(
                $"Idempotency key '{fields.IdempotencyKey}' of resource type '{fields.ResourceType}' already records a different event " +
                $"(Action='{existing.Action}', ResourceId='{existing.ResourceId}', Outcome={existing.Outcome}); " +
                $"it cannot also record Action='{fields.Action}', ResourceId='{fields.ResourceId}', Outcome={fields.Outcome}.");
        }

        AuditingLog.IdempotentDuplicateReturned(_logger, fields.ResourceType, fields.IdempotencyKey!);
        AuditingMeter.RecordIdempotentDuplicate();
        return existing;
    }

    internal static AuditRecord ToRecord(PendingLedgerRecord pending)
    {
        var f = pending.Fields;
        return new AuditRecord
        {
            Id = f.Id,
            TenantId = f.TenantId,
            ResourceType = f.ResourceType,
            ResourceId = f.ResourceId,
            Action = f.Action,
            Outcome = f.Outcome,
            ErrorCode = f.ErrorCode,
            ActorId = f.ActorId,
            ActorKind = f.ActorKind,
            ClientId = f.ClientId,
            SessionId = f.SessionId,
            ImpersonatorId = f.ImpersonatorId,
            SourceService = f.SourceService,
            CorrelationId = f.CorrelationId,
            TraceId = f.TraceId,
            ApprovalId = f.ApprovalId,
            IdempotencyKey = f.IdempotencyKey,
            OccurredOn = f.OccurredOn,
            BeforeSnapshot = pending.BeforeSnapshot,
            AfterSnapshot = pending.AfterSnapshot,
        };
    }
}

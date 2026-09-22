using System.Data;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using SharedKernel.Contracts.Pagination;
using SharedKernel.Persistence.Abstractions.Connections;
using SharedKernel.Persistence.EfCore.Auditing.Checkpoints;
using SharedKernel.Persistence.EfCore.Auditing.Storage;
using SharedKernel.Persistence.EfCore.Auditing.Verification;
using SharedKernel.Persistence.EfCore.Auditing.Writing;

namespace SharedKernel.Persistence.EfCore.Auditing.Querying;

/// <summary>The default <see cref="IAuditQueryService"/>: raw SQL over the ledger tables.</summary>
internal sealed class EfAuditQueryService(
    IDbConnectionFactory connectionFactory,
    AuditCheckpointWriter checkpoints,
    LedgerSelfAudit selfAudit) : IAuditQueryService
{
    private AuditCallerScope Scope => selfAudit.Factory.Scope;

    /// <inheritdoc />
    public Task<CursorPagedList<AuditRecord>> QueryAsync(AuditRecordQuery query, CancellationToken cancellationToken = default)
    {
        Validate(query, crossTenant: false);
        var tenantId = Scope.ResolveCallerTenant("Querying the audit ledger");
        return PageAsync(query, tenantFilter: true, tenantId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<CursorPagedList<AuditRecord>> QueryAcrossTenantsAsync(AuditRecordQuery query, CancellationToken cancellationToken = default)
    {
        Scope.RequireCrossTenantScope(nameof(QueryAcrossTenantsAsync));
        Validate(query, crossTenant: true);

        await selfAudit.RecordAsync(
            tenantId: null,
            AuditLedgerActions.LedgerResourceType,
            $"{query.ResourceType}/{query.ResourceId}",
            AuditLedgerActions.CrossTenantQueried,
            new Dictionary<string, string?>
            {
                ["resourceType"] = query.ResourceType,
                ["resourceId"] = query.ResourceId,
                ["from"] = Format(query.From),
                ["to"] = Format(query.To),
            },
            connection: null,
            transaction: null,
            cancellationToken).ConfigureAwait(false);

        return await PageAsync(query, tenantFilter: false, tenantId: null, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<AuditRecord> ExportRangeAsync(
        string resourceType,
        DateTimeOffset from,
        DateTimeOffset to,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceType);
        if (from > to)
            throw new ArgumentException("The export range starts after it ends.", nameof(from));

        var tenantId = Scope.ResolveCallerTenant("Exporting the audit ledger");

        await selfAudit.RecordAsync(
            tenantId,
            AuditLedgerActions.LedgerResourceType,
            resourceType,
            AuditLedgerActions.Exported,
            new Dictionary<string, string?> { ["resourceType"] = resourceType, ["from"] = Format(from), ["to"] = Format(to) },
            connection: null,
            transaction: null,
            cancellationToken).ConfigureAwait(false);

        var connection = await connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            await using var command = LedgerDb.CreateCommand(connection, null, string.Empty);
            var tenant = LedgerDb.TenantPredicate(command, "r.tenant_id", tenantId);
            LedgerDb.Add(command, "@resource_type", resourceType, DbType.String);
            LedgerDb.Add(command, "@from", from.ToUniversalTime(), DbType.DateTimeOffset);
            LedgerDb.Add(command, "@to", to.ToUniversalTime(), DbType.DateTimeOffset);
            command.CommandText = LedgerDb.ReadModelSelect +
                $"WHERE {tenant} AND r.resource_type = @resource_type AND r.occurred_on BETWEEN @from AND @to ORDER BY r.occurred_on, r.id";

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                yield return LedgerDb.ReadRecord(reader);
        }
    }

    /// <inheritdoc />
    public Task<AuditChainVerificationResult> VerifyChainAsync(string resourceType, bool requirePayloads = false, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceType);
        var tenantId = Scope.ResolveCallerTenant("Verifying an audit chain");
        return checkpoints.Verifier.VerifyAsync(new ChainId(tenantId, resourceType), null, null, requirePayloads, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<AuditChainVerificationResult> VerifyChainFromCheckpointAsync(
        AuditChainCheckpoint checkpoint,
        AuditChainCheckpoint? expectedHead = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);

        if (expectedHead is not null &&
            (expectedHead.TenantId != checkpoint.TenantId ||
             !string.Equals(expectedHead.ResourceType, checkpoint.ResourceType, StringComparison.Ordinal)))
        {
            throw new ArgumentException("The expected head names a different chain than the checkpoint.", nameof(expectedHead));
        }

        Scope.RequireAccess(checkpoint.TenantId, nameof(VerifyChainFromCheckpointAsync));

        AuditChainCheckpoint[] candidates = expectedHead is null ? [checkpoint] : [checkpoint, expectedHead];
        foreach (var candidate in candidates)
        {
            if (!await checkpoints.IsAuthenticAsync(candidate, cancellationToken).ConfigureAwait(false))
            {
                throw new ArgumentException(
                    $"Checkpoint '{candidate.Id}' (sequence {candidate.Sequence}) is not authentic: its signature does not verify, or " +
                    $"its signing key '{candidate.SigningKeyId}' is not one of {nameof(AuditLedgerOptions.AcceptedCheckpointSigningKeyIds)}.",
                    candidate == checkpoint ? nameof(checkpoint) : nameof(expectedHead));
            }
        }

        return await checkpoints.Verifier.VerifyAsync(
            new ChainId(checkpoint.TenantId, checkpoint.ResourceType),
            new ChainAnchor(checkpoint.Sequence, checkpoint.HeadMac),
            expectedHead is null ? null : new ChainAnchor(expectedHead.Sequence, expectedHead.HeadMac),
            requirePayloads: false,
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<AuditRecordVerificationResult?> VerifyRecordAsync(Guid recordId, bool requirePayload = false, CancellationToken cancellationToken = default)
    {
        var connection = await connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var record = await LedgerDb.ReadByIdAsync(connection, null, recordId, cancellationToken).ConfigureAwait(false);
            if (record is null || !Scope.CanAccess(record.TenantId))
                return null;
        }

        return await checkpoints.Verifier.VerifyRecordAsync(recordId, requirePayload, cancellationToken).ConfigureAwait(false);
    }

    private async Task<CursorPagedList<AuditRecord>> PageAsync(
        AuditRecordQuery query,
        bool tenantFilter,
        Guid? tenantId,
        CancellationToken cancellationToken)
    {
        var connection = await connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            await using var command = LedgerDb.CreateCommand(connection, null, string.Empty);
            var where = new StringBuilder("WHERE TRUE");

            if (tenantFilter)
                where.Append(" AND ").Append(LedgerDb.TenantPredicate(command, "r.tenant_id", tenantId));
            if (query.ResourceType is { } resourceType)
            {
                where.Append(" AND r.resource_type = @resource_type");
                LedgerDb.Add(command, "@resource_type", resourceType, DbType.String);
            }

            if (query.ResourceId is { } resourceId)
            {
                where.Append(" AND r.resource_id = @resource_id");
                LedgerDb.Add(command, "@resource_id", resourceId, DbType.String);
            }

            if (query.ActorId is { } actorId)
            {
                where.Append(" AND r.actor_id = @actor_id");
                LedgerDb.Add(command, "@actor_id", actorId, DbType.String);
            }

            if (query.From is { } from)
            {
                where.Append(" AND r.occurred_on >= @from");
                LedgerDb.Add(command, "@from", from.ToUniversalTime(), DbType.DateTimeOffset);
            }

            if (query.To is { } to)
            {
                where.Append(" AND r.occurred_on <= @to");
                LedgerDb.Add(command, "@to", to.ToUniversalTime(), DbType.DateTimeOffset);
            }

            if (query.Cursor is not null)
            {
                var position = PageCursor.Decode<DateTimeOffset, Guid>(query.Cursor);
                if (position.IsFailure)
                    throw new ArgumentException("The audit query cursor is invalid.", nameof(query));

                where.Append(query.Descending ? " AND (r.occurred_on, r.id) < (@after_key, @after_id)" : " AND (r.occurred_on, r.id) > (@after_key, @after_id)");
                LedgerDb.Add(command, "@after_key", position.Value.Key.ToUniversalTime(), DbType.DateTimeOffset);
                LedgerDb.Add(command, "@after_id", position.Value.Id, DbType.Guid);
            }

            var direction = query.Descending ? "DESC" : "ASC";
            LedgerDb.Add(command, "@take", query.Limit + 1, DbType.Int32);
            command.CommandText = LedgerDb.ReadModelSelect + where + $" ORDER BY r.occurred_on {direction}, r.id {direction} LIMIT @take";

            var rows = new List<AuditRecord>(query.Limit + 1);
            await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
            {
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    rows.Add(LedgerDb.ReadRecord(reader));
            }

            return CursorPagedList<AuditRecord>.FromLookahead(rows, query.Limit, last => PageCursor.Encode(last.OccurredOn, last.Id));
        }
    }

    private static void Validate(AuditRecordQuery query, bool crossTenant)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (query.Limit is < 1 or > AuditQueryLimits.MaxPageSize)
            throw new ArgumentOutOfRangeException(nameof(query), query.Limit, $"Limit must be between 1 and {AuditQueryLimits.MaxPageSize}.");
        if (query.ResourceId is not null && query.ResourceType is null)
            throw new ArgumentException($"{nameof(AuditRecordQuery.ResourceId)} requires {nameof(AuditRecordQuery.ResourceType)}.", nameof(query));
        if (query.ResourceType is null && query.ActorId is null)
            throw new ArgumentException($"An audit query needs {nameof(AuditRecordQuery.ResourceType)} or {nameof(AuditRecordQuery.ActorId)}.", nameof(query));
        if (crossTenant && (query.ResourceType is null || query.ResourceId is null))
            throw new ArgumentException($"A cross-tenant audit query needs {nameof(AuditRecordQuery.ResourceType)} and {nameof(AuditRecordQuery.ResourceId)}.", nameof(query));
        if (query.From is { } from && query.To is { } to && from > to)
            throw new ArgumentException("The query range starts after it ends.", nameof(query));
    }

    private static string? Format(DateTimeOffset? value) => value?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
}

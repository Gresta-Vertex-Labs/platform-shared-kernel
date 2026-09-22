using System.Data;
using Microsoft.Extensions.Logging;
using SharedKernel.Application.Auditing;
using SharedKernel.Persistence.Abstractions.Connections;
using SharedKernel.Persistence.EfCore.Auditing.Checkpoints;
using SharedKernel.Persistence.EfCore.Auditing.Diagnostics;
using SharedKernel.Persistence.EfCore.Auditing.Sealing;
using SharedKernel.Persistence.EfCore.Auditing.Storage;
using SharedKernel.Persistence.EfCore.Auditing.Writing;

namespace SharedKernel.Persistence.EfCore.Auditing.Maintenance;

/// <summary>The default <see cref="IAuditLedgerMaintenance"/>.</summary>
internal sealed class AuditLedgerMaintenance(
    IDbConnectionFactory connectionFactory,
    AuditSealingEngine sealer,
    AuditCheckpointWriter checkpoints,
    IAuditRecordAuthenticator authenticator,
    LedgerSelfAudit selfAudit,
    ILogger<AuditLedgerMaintenance> logger) : IAuditLedgerMaintenance
{
    private const int ResealAttempts = 20;
    private static readonly TimeSpan ResealRetryDelay = TimeSpan.FromMilliseconds(250);

    private AuditCallerScope Scope => selfAudit.Factory.Scope;

    /// <inheritdoc />
    public Task<AuditSealPassResult> SealPendingAsync(CancellationToken cancellationToken = default) =>
        sealer.SealUntilDrainedAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<AuditResealResult> SealAllChainsAsync(string reason, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        Scope.RequireCrossTenantScope(nameof(SealAllChainsAsync));
        RequireReason(reason);

        var key = await authenticator.GetCurrentKeyAsync(cancellationToken).ConfigureAwait(false);
        var chains = new List<ChainId>();
        var markers = new List<Guid>();

        var connection = await connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            await using (var command = LedgerDb.CreateCommand(connection, null,
                $"SELECT DISTINCT tenant_id, resource_type FROM {AuditLedgerSchema.LinksTable}"))
            await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
            {
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    chains.Add(new ChainId(reader.IsDBNull(0) ? null : reader.GetGuid(0), reader.GetString(1)));
            }

            var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            await using (transaction.ConfigureAwait(false))
            {
                foreach (var chain in chains)
                {
                    var marker = await selfAudit.RecordAsync(
                        chain.TenantId,
                        chain.ResourceType,
                        "*",
                        AuditLedgerActions.ChainResealed,
                        new Dictionary<string, string?> { ["reason"] = reason, ["keyId"] = key.Id },
                        connection,
                        transaction,
                        cancellationToken).ConfigureAwait(false);
                    markers.Add(marker.Id);
                }

                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }

            var sealedCount = 0;
            var fullySealed = markers.Count == 0;
            for (var attempt = 0; attempt < ResealAttempts && !fullySealed; attempt++)
            {
                if (attempt > 0)
                    await Task.Delay(ResealRetryDelay, cancellationToken).ConfigureAwait(false);

                sealedCount += (await sealer.SealUntilDrainedAsync(cancellationToken).ConfigureAwait(false)).RecordsSealed;
                fullySealed = await CountUnsealedAsync(connection, markers, cancellationToken).ConfigureAwait(false) == 0;
            }

            var emitted = 0;
            if (fullySealed && checkpoints.CanSign)
            {
                foreach (var chain in chains)
                {
                    try
                    {
                        if (await checkpoints.CreateAsync(chain, skipIfUnchanged: true, cancellationToken).ConfigureAwait(false) is not null)
                            emitted++;
                    }
                    catch (Exception ex) when (ex is AuditChainIntegrityException or InvalidOperationException)
                    {
                        AuditingLog.CheckpointEmissionFailed(logger, ex, chain.TenantLabel, chain.ResourceType);
                    }
                }
            }

            AuditingLog.ChainsResealed(logger, chains.Count, key.Id, sealedCount, emitted);
            return new AuditResealResult(chains.Count, sealedCount, emitted, fullySealed);
        }
    }

    /// <inheritdoc />
    public async Task<bool> ErasePayloadAsync(Guid recordId, string reason, CancellationToken cancellationToken = default)
    {
        RequireReason(reason);

        var connection = await connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            await using (transaction.ConfigureAwait(false))
            {
                var record = await LedgerDb.ReadByIdAsync(connection, transaction, recordId, cancellationToken).ConfigureAwait(false);
                if (record is null || !Scope.CanAccess(record.TenantId))
                    throw new KeyNotFoundException($"Audit record '{recordId}' does not exist in the caller's tenant.");

                await using var command = LedgerDb.CreateCommand(connection, transaction,
                    $"DELETE FROM {AuditLedgerSchema.PayloadsTable} WHERE record_id = @id");
                LedgerDb.Add(command, "@id", recordId, DbType.Guid);
                var erased = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) > 0;

                if (erased)
                {
                    await selfAudit.RecordAsync(
                        record.TenantId,
                        AuditLedgerActions.LedgerResourceType,
                        recordId.ToString("D"),
                        AuditLedgerActions.PayloadErased,
                        new Dictionary<string, string?> { ["recordId"] = recordId.ToString("D"), ["resourceType"] = record.ResourceType, ["reason"] = reason },
                        connection,
                        transaction,
                        cancellationToken).ConfigureAwait(false);
                }

                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

                if (erased)
                {
                    AuditingMeter.RecordPayloadsErased(1);
                    AuditingLog.PayloadsErased(logger, 1, record.ResourceType);
                }

                return erased;
            }
        }
    }

    /// <inheritdoc />
    public async Task<int> EraseResourcePayloadsAsync(string resourceType, string resourceId, string reason, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceType);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);
        RequireReason(reason);

        var tenantId = Scope.ResolveCallerTenant("Erasing audit payloads");

        var connection = await connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            await using (transaction.ConfigureAwait(false))
            {
                await using var command = LedgerDb.CreateCommand(connection, transaction, string.Empty);
                var tenant = LedgerDb.TenantPredicate(command, "r.tenant_id", tenantId);
                LedgerDb.Add(command, "@resource_type", resourceType, DbType.String);
                LedgerDb.Add(command, "@resource_id", resourceId, DbType.String);
                command.CommandText =
                    $"""
                    DELETE FROM {AuditLedgerSchema.PayloadsTable} p
                    USING {AuditLedgerSchema.RecordsTable} r
                    WHERE p.record_id = r.id AND {tenant} AND r.resource_type = @resource_type AND r.resource_id = @resource_id
                    """;
                var erased = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

                if (erased > 0)
                {
                    await selfAudit.RecordAsync(
                        tenantId,
                        AuditLedgerActions.LedgerResourceType,
                        $"{resourceType}/{resourceId}",
                        AuditLedgerActions.PayloadErased,
                        new Dictionary<string, string?> { ["resourceType"] = resourceType, ["resourceId"] = resourceId, ["count"] = erased.ToString(System.Globalization.CultureInfo.InvariantCulture), ["reason"] = reason },
                        connection,
                        transaction,
                        cancellationToken).ConfigureAwait(false);
                }

                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

                if (erased > 0)
                {
                    AuditingMeter.RecordPayloadsErased(erased);
                    AuditingLog.PayloadsErased(logger, erased, resourceType);
                }

                return erased;
            }
        }
    }

    private static async Task<int> CountUnsealedAsync(System.Data.Common.DbConnection connection, List<Guid> ids, CancellationToken cancellationToken)
    {
        await using var command = LedgerDb.CreateCommand(connection, null,
            $"SELECT count(*) FROM unnest(@ids) AS m(id) WHERE NOT EXISTS (SELECT 1 FROM {AuditLedgerSchema.LinksTable} l WHERE l.record_id = m.id)");
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@ids";
        parameter.Value = ids.ToArray();
        command.Parameters.Add(parameter);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static void RequireReason(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (reason.Length > AuditFieldLimits.ErrorCode)
            throw new ArgumentException($"The reason is longer than {AuditFieldLimits.ErrorCode} characters.", nameof(reason));
    }
}

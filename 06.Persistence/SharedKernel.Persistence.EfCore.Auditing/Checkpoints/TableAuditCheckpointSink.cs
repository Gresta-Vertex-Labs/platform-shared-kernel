using System.Data;
using SharedKernel.Persistence.Abstractions.Connections;
using SharedKernel.Persistence.EfCore.Auditing.Format;
using SharedKernel.Persistence.EfCore.Auditing.Storage;

namespace SharedKernel.Persistence.EfCore.Auditing;

/// <summary>
/// The default <see cref="IAuditCheckpointSink"/>: the insert-only <c>audit_checkpoints</c> table in the
/// ledger database.
/// </summary>
/// <remarks>
/// Convenient, but it lives next to the data it protects: an attacker who can rewrite the ledger and also
/// disable the table's triggers can rewrite checkpoints too (the signatures still cannot be forged without
/// the signing key). For stronger guarantees register a sink that writes to object-locked (WORM) storage.
/// </remarks>
internal sealed class TableAuditCheckpointSink : IAuditCheckpointSink
{
    private readonly IDbConnectionFactory _connectionFactory;

    /// <summary>Initialises a new <see cref="TableAuditCheckpointSink"/>.</summary>
    /// <param name="connectionFactory">The ledger database's connection factory.</param>
    public TableAuditCheckpointSink(IDbConnectionFactory connectionFactory)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);
        _connectionFactory = connectionFactory;
    }

    /// <inheritdoc />
    public async Task AppendAsync(AuditChainCheckpoint checkpoint, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);

        var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            await using var command = LedgerDb.CreateCommand(connection, null,
                $"""
                INSERT INTO {AuditLedgerSchema.CheckpointsTable}
                    (id, tenant_id, resource_type, sequence, head_mac, created_on, signing_key_id, signature, format_version)
                VALUES (@id, @tenant_id, @resource_type, @sequence, @head_mac, @created_on, @signing_key_id, @signature, @format_version)
                """);
            LedgerDb.Add(command, "@id", checkpoint.Id, DbType.Guid);
            LedgerDb.Add(command, "@tenant_id", checkpoint.TenantId, DbType.Guid);
            LedgerDb.Add(command, "@resource_type", checkpoint.ResourceType, DbType.String);
            LedgerDb.Add(command, "@sequence", checkpoint.Sequence, DbType.Int64);
            LedgerDb.Add(command, "@head_mac", checkpoint.HeadMac, DbType.Binary);
            LedgerDb.Add(command, "@created_on", checkpoint.CreatedOn.ToUniversalTime(), DbType.DateTimeOffset);
            LedgerDb.Add(command, "@signing_key_id", checkpoint.SigningKeyId, DbType.String);
            LedgerDb.Add(command, "@signature", checkpoint.Signature, DbType.Binary);
            LedgerDb.Add(command, "@format_version", AuditV3Format.Version, DbType.Int32);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task<AuditChainCheckpoint?> GetLatestAsync(Guid? tenantId, string resourceType, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceType);

        var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            await using var command = LedgerDb.CreateCommand(connection, null, string.Empty);
            var tenant = LedgerDb.TenantPredicate(command, "tenant_id", tenantId);
            LedgerDb.Add(command, "@resource_type", resourceType, DbType.String);
            command.CommandText =
                $"""
                SELECT id, tenant_id, resource_type, sequence, head_mac, created_on, signing_key_id, signature
                FROM {AuditLedgerSchema.CheckpointsTable}
                WHERE {tenant} AND resource_type = @resource_type
                ORDER BY sequence DESC, created_on DESC
                LIMIT 1
                """;

            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                return null;

            return new AuditChainCheckpoint
            {
                Id = reader.GetGuid(0),
                TenantId = reader.IsDBNull(1) ? null : reader.GetGuid(1),
                ResourceType = reader.GetString(2),
                Sequence = reader.GetInt64(3),
                HeadMac = reader.GetFieldValue<byte[]>(4),
                CreatedOn = reader.GetFieldValue<DateTimeOffset>(5),
                SigningKeyId = reader.GetString(6),
                Signature = reader.GetFieldValue<byte[]>(7),
            };
        }
    }
}

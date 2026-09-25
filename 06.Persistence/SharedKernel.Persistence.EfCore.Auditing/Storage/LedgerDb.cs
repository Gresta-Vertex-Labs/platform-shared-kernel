using System.Data;
using System.Data.Common;
using SharedKernel.Execution.Auditing;
using SharedKernel.Execution.Context;
using SharedKernel.Persistence.EfCore.Auditing.Format;

namespace SharedKernel.Persistence.EfCore.Auditing.Storage;

/// <summary>A record ready to insert: its stored fields plus the erasable payload.</summary>
internal sealed record PendingLedgerRecord(LedgerRecordFields Fields, byte[] Salt, string? BeforeSnapshot, string? AfterSnapshot);

/// <summary>The chain identity <c>(TenantId, ResourceType)</c>.</summary>
internal readonly record struct ChainId(Guid? TenantId, string ResourceType)
{
    public string TenantLabel => TenantId?.ToString("D") ?? "system";
}

/// <summary>
/// Raw ADO.NET access to the ledger tables: parameter helpers, the one record column list and its
/// mapping, and the request-path insert. PostgreSQL-only SQL.
/// </summary>
internal static class LedgerDb
{
    /// <summary>The <c>audit_records</c> columns, in the ordinal order <see cref="ReadFields"/> expects, qualified by alias <c>r</c>.</summary>
    public const string RecordColumns =
        "r.id, r.tenant_id, r.resource_type, r.resource_id, r.action, r.outcome, r.error_code, r.actor_id, r.actor_kind, " +
        "r.client_id, r.session_id, r.impersonator_id, r.source_service, r.correlation_id, r.trace_id, r.approval_id, " +
        "r.idempotency_key, r.occurred_on, r.payload_hash, r.format_version, r.insert_xid";

    /// <summary>The number of columns in <see cref="RecordColumns"/>.</summary>
    public const int RecordColumnCount = 21;

    /// <summary>
    /// <see cref="RecordColumns"/> plus payload and seal columns, ordinal <see cref="RecordColumnCount"/> onward:
    /// before, after, payload-present flag, sequence, sealed_on, key_id.
    /// </summary>
    public const string ReadModelSelect =
        "SELECT " + RecordColumns + ", p.before_snapshot, p.after_snapshot, (p.record_id IS NOT NULL) AS has_payload, " +
        "l.sequence, l.sealed_on, l.key_id " +
        "FROM " + AuditLedgerSchema.RecordsTable + " r " +
        "LEFT JOIN " + AuditLedgerSchema.PayloadsTable + " p ON p.record_id = r.id " +
        "LEFT JOIN " + AuditLedgerSchema.LinksTable + " l ON l.record_id = r.id ";

    public static DbParameter Add(DbCommand command, string name, object? value, DbType type)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.DbType = type;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
        return parameter;
    }

    /// <summary>Returns <c>{column} = @name</c> (binding <paramref name="tenantId"/>) or <c>{column} IS NULL</c>.</summary>
    public static string TenantPredicate(DbCommand command, string column, Guid? tenantId, string parameterName = "@tenant")
    {
        if (tenantId is not { } id)
            return $"{column} IS NULL";

        Add(command, parameterName, id, DbType.Guid);
        return $"{column} = {parameterName}";
    }

    public static DbCommand CreateCommand(DbConnection connection, DbTransaction? transaction, string sql)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        return command;
    }

    public static LedgerRecordFields ReadFields(DbDataReader reader) => new()
    {
        Id = reader.GetGuid(0),
        TenantId = reader.IsDBNull(1) ? null : reader.GetGuid(1),
        ResourceType = reader.GetString(2),
        ResourceId = reader.GetString(3),
        Action = reader.GetString(4),
        Outcome = (AuditOutcome)reader.GetInt16(5),
        ErrorCode = GetNullableString(reader, 6),
        ActorId = reader.GetString(7),
        ActorKind = (ActorKind)reader.GetInt16(8),
        ClientId = GetNullableString(reader, 9),
        SessionId = GetNullableString(reader, 10),
        ImpersonatorId = GetNullableString(reader, 11),
        SourceService = reader.GetString(12),
        CorrelationId = GetNullableString(reader, 13),
        TraceId = GetNullableString(reader, 14),
        ApprovalId = GetNullableString(reader, 15),
        IdempotencyKey = GetNullableString(reader, 16),
        OccurredOn = reader.GetFieldValue<DateTimeOffset>(17),
        PayloadHash = reader.GetFieldValue<byte[]>(18),
        FormatVersion = reader.GetInt32(19),
    };

    /// <summary>Maps a row selected with <see cref="ReadModelSelect"/>.</summary>
    public static AuditRecord ReadRecord(DbDataReader reader)
    {
        var fields = ReadFields(reader);
        const int o = RecordColumnCount;
        var hasPayload = reader.GetBoolean(o + 2);

        return new AuditRecord
        {
            Id = fields.Id,
            TenantId = fields.TenantId,
            ResourceType = fields.ResourceType,
            ResourceId = fields.ResourceId,
            Action = fields.Action,
            Outcome = fields.Outcome,
            ErrorCode = fields.ErrorCode,
            ActorId = fields.ActorId,
            ActorKind = fields.ActorKind,
            ClientId = fields.ClientId,
            SessionId = fields.SessionId,
            ImpersonatorId = fields.ImpersonatorId,
            SourceService = fields.SourceService,
            CorrelationId = fields.CorrelationId,
            TraceId = fields.TraceId,
            ApprovalId = fields.ApprovalId,
            IdempotencyKey = fields.IdempotencyKey,
            OccurredOn = fields.OccurredOn,
            BeforeSnapshot = GetNullableString(reader, o),
            AfterSnapshot = GetNullableString(reader, o + 1),
            PayloadErased = !hasPayload,
            Sequence = reader.IsDBNull(o + 3) ? null : reader.GetInt64(o + 3),
            SealedOn = reader.IsDBNull(o + 4) ? null : reader.GetFieldValue<DateTimeOffset>(o + 4),
            KeyId = GetNullableString(reader, o + 5),
        };
    }

    public static string? GetNullableString(DbDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    public static byte[]? GetNullableBytes(DbDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetFieldValue<byte[]>(ordinal);

    /// <summary>
    /// Inserts the record and its payload in one statement. Returns <see langword="false"/> when a record
    /// with the same <c>(tenant, resource type, idempotency key)</c> already exists (nothing is written).
    /// Never aborts the surrounding transaction on an idempotency conflict (<c>ON CONFLICT DO NOTHING</c>).
    /// </summary>
    public static async Task<bool> InsertAsync(
        DbConnection connection,
        DbTransaction? transaction,
        PendingLedgerRecord pending,
        CancellationToken cancellationToken)
    {
        var r = pending.Fields;
        await using var command = CreateCommand(connection, transaction,
            $"""
            WITH inserted AS (
                INSERT INTO {AuditLedgerSchema.RecordsTable} (
                    id, tenant_id, resource_type, resource_id, action, outcome, error_code, actor_id, actor_kind,
                    client_id, session_id, impersonator_id, source_service, correlation_id, trace_id, approval_id,
                    idempotency_key, occurred_on, payload_hash, format_version)
                VALUES (
                    @id, @tenant_id, @resource_type, @resource_id, @action, @outcome, @error_code, @actor_id, @actor_kind,
                    @client_id, @session_id, @impersonator_id, @source_service, @correlation_id, @trace_id, @approval_id,
                    @idempotency_key, @occurred_on, @payload_hash, @format_version)
                ON CONFLICT (tenant_id, resource_type, idempotency_key) WHERE idempotency_key IS NOT NULL DO NOTHING
                RETURNING id)
            INSERT INTO {AuditLedgerSchema.PayloadsTable} (record_id, salt, before_snapshot, after_snapshot)
            SELECT id, @salt, @before_snapshot, @after_snapshot FROM inserted
            RETURNING record_id
            """);

        Add(command, "@id", r.Id, DbType.Guid);
        Add(command, "@tenant_id", r.TenantId, DbType.Guid);
        Add(command, "@resource_type", r.ResourceType, DbType.String);
        Add(command, "@resource_id", r.ResourceId, DbType.String);
        Add(command, "@action", r.Action, DbType.String);
        Add(command, "@outcome", (short)r.Outcome, DbType.Int16);
        Add(command, "@error_code", r.ErrorCode, DbType.String);
        Add(command, "@actor_id", r.ActorId, DbType.String);
        Add(command, "@actor_kind", (short)r.ActorKind, DbType.Int16);
        Add(command, "@client_id", r.ClientId, DbType.String);
        Add(command, "@session_id", r.SessionId, DbType.String);
        Add(command, "@impersonator_id", r.ImpersonatorId, DbType.String);
        Add(command, "@source_service", r.SourceService, DbType.String);
        Add(command, "@correlation_id", r.CorrelationId, DbType.String);
        Add(command, "@trace_id", r.TraceId, DbType.String);
        Add(command, "@approval_id", r.ApprovalId, DbType.String);
        Add(command, "@idempotency_key", r.IdempotencyKey, DbType.String);
        Add(command, "@occurred_on", r.OccurredOn, DbType.DateTimeOffset);
        Add(command, "@payload_hash", r.PayloadHash, DbType.Binary);
        Add(command, "@format_version", r.FormatVersion, DbType.Int32);
        Add(command, "@salt", pending.Salt, DbType.Binary);
        Add(command, "@before_snapshot", pending.BeforeSnapshot, DbType.String);
        Add(command, "@after_snapshot", pending.AfterSnapshot, DbType.String);

        var inserted = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return inserted is Guid;
    }

    /// <summary>Reads the record stored under an idempotency key in a chain, or <see langword="null"/>.</summary>
    public static async Task<AuditRecord?> ReadByIdempotencyKeyAsync(
        DbConnection connection,
        DbTransaction? transaction,
        ChainId chain,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(connection, transaction, string.Empty);
        var tenant = TenantPredicate(command, "r.tenant_id", chain.TenantId);
        Add(command, "@resource_type", chain.ResourceType, DbType.String);
        Add(command, "@idempotency_key", idempotencyKey, DbType.String);
        command.CommandText = ReadModelSelect +
            $"WHERE {tenant} AND r.resource_type = @resource_type AND r.idempotency_key = @idempotency_key LIMIT 1";

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? ReadRecord(reader) : null;
    }

    /// <summary>Reads one record by id, or <see langword="null"/>.</summary>
    public static async Task<AuditRecord?> ReadByIdAsync(
        DbConnection connection,
        DbTransaction? transaction,
        Guid id,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(connection, transaction, ReadModelSelect + "WHERE r.id = @id");
        Add(command, "@id", id, DbType.Guid);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? ReadRecord(reader) : null;
    }
}

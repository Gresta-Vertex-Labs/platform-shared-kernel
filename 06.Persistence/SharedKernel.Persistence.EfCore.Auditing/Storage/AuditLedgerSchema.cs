using System.Text;

namespace SharedKernel.Persistence.EfCore.Auditing;

/// <summary>
/// The PostgreSQL schema of the audit ledger: four tables, their indexes and the triggers that make them
/// append-only. <see cref="CreateScript"/> is what <c>migrationBuilder.CreateAuditLedgerTable()</c> emits;
/// it is idempotent, so it can also be run directly in tests or tooling.
/// </summary>
/// <remarks>
/// <list type="table">
/// <item><term><see cref="RecordsTable"/></term><description>One row per audited action, written by the request path with a plain INSERT. Append-only (UPDATE/DELETE/TRUNCATE rejected). A BEFORE INSERT trigger stamps the inserting transaction id (<c>insert_xid</c>) the sealer orders by.</description></item>
/// <item><term><see cref="PayloadsTable"/></term><description>The erasable snapshots and salt. UPDATE/TRUNCATE rejected; DELETE is the erasure operation.</description></item>
/// <item><term><see cref="LinksTable"/></term><description>Insert-only seals written by the sealer: chain sequence, previous MAC, MAC, key id. Append-only.</description></item>
/// <item><term><see cref="CheckpointsTable"/></term><description>The default <c>IAuditCheckpointSink</c>: signed chain heads. Append-only.</description></item>
/// </list>
/// Requires PostgreSQL 15 or later (<c>NULLS NOT DISTINCT</c>).
/// </remarks>
public static class AuditLedgerSchema
{
    /// <summary>The records table.</summary>
    public const string RecordsTable = "audit_records";

    /// <summary>The erasable payloads table.</summary>
    public const string PayloadsTable = "audit_record_payloads";

    /// <summary>The chain links (seals) table.</summary>
    public const string LinksTable = "audit_chain_links";

    /// <summary>The default checkpoint sink's table.</summary>
    public const string CheckpointsTable = "audit_checkpoints";

    internal const string StampXidFunction = "audit_records_stamp_insert_xid";
    internal const string StampXidTrigger = "audit_records_stamp_insert_xid";

    /// <summary>Gets the idempotent DDL creating every table, index and trigger of the ledger.</summary>
    public static string CreateScript => Join(CreateStatements);

    /// <summary>Gets the DDL dropping the ledger (tables, trigger functions). Destroys all audit data.</summary>
    public static string DropScript => Join(DropStatements);

    /// <summary>Gets the ledger tables, in creation order.</summary>
    public static IReadOnlyList<string> Tables { get; } = [RecordsTable, PayloadsTable, LinksTable, CheckpointsTable];

    /// <summary>The individual statements of <see cref="CreateScript"/>.</summary>
    internal static IReadOnlyList<string> CreateStatements { get; } = BuildCreateStatements();

    /// <summary>The individual statements of <see cref="DropScript"/>.</summary>
    internal static IReadOnlyList<string> DropStatements { get; } =
    [
        $"DROP TABLE IF EXISTS {CheckpointsTable};",
        $"DROP TABLE IF EXISTS {LinksTable};",
        $"DROP TABLE IF EXISTS {PayloadsTable};",
        $"DROP TABLE IF EXISTS {RecordsTable};",
        $"DROP FUNCTION IF EXISTS {StampXidFunction}();",
        .. Tables.Select(t => $"DROP FUNCTION IF EXISTS {ImmutabilityTriggerSql.FunctionName(null, t)}();"),
    ];

    /// <summary>The trigger names the self-check expects per table (all <c>ENABLE ALWAYS</c>).</summary>
    internal static IReadOnlyDictionary<string, IReadOnlyList<string>> ExpectedTriggers { get; } = new Dictionary<string, IReadOnlyList<string>>
    {
        [RecordsTable] = [.. ImmutabilityTriggerSql.TriggerNames(RecordsTable, allowDelete: false), StampXidTrigger],
        [PayloadsTable] = ImmutabilityTriggerSql.TriggerNames(PayloadsTable, allowDelete: true),
        [LinksTable] = ImmutabilityTriggerSql.TriggerNames(LinksTable, allowDelete: false),
        [CheckpointsTable] = ImmutabilityTriggerSql.TriggerNames(CheckpointsTable, allowDelete: false),
    };

    private static List<string> BuildCreateStatements()
    {
        List<string> statements =
        [
            $"""
            CREATE TABLE IF NOT EXISTS {RecordsTable} (
                id uuid PRIMARY KEY,
                tenant_id uuid NULL,
                resource_type text NOT NULL,
                resource_id text NOT NULL,
                action text NOT NULL,
                outcome smallint NOT NULL,
                error_code text NULL,
                actor_id text NOT NULL,
                actor_kind smallint NOT NULL,
                client_id text NULL,
                session_id text NULL,
                impersonator_id text NULL,
                source_service text NOT NULL,
                correlation_id text NULL,
                trace_id text NULL,
                approval_id text NULL,
                idempotency_key text NULL,
                occurred_on timestamptz NOT NULL,
                payload_hash bytea NOT NULL CHECK (octet_length(payload_hash) = 32),
                format_version integer NOT NULL,
                insert_xid bigint NOT NULL DEFAULT (pg_current_xact_id()::text::bigint)
            );
            """,
            $"""
            CREATE TABLE IF NOT EXISTS {PayloadsTable} (
                record_id uuid PRIMARY KEY REFERENCES {RecordsTable} (id),
                salt bytea NOT NULL CHECK (octet_length(salt) = 32),
                before_snapshot text NULL,
                after_snapshot text NULL
            );
            """,
            $"""
            CREATE TABLE IF NOT EXISTS {LinksTable} (
                record_id uuid PRIMARY KEY REFERENCES {RecordsTable} (id),
                tenant_id uuid NULL,
                resource_type text NOT NULL,
                sequence bigint NOT NULL CHECK (sequence >= 1),
                previous_mac bytea NULL,
                mac bytea NOT NULL,
                key_id text NOT NULL,
                algorithm text NOT NULL,
                format_version integer NOT NULL,
                record_insert_xid bigint NOT NULL,
                sealed_on timestamptz NOT NULL,
                CHECK ((sequence = 1) = (previous_mac IS NULL))
            );
            """,
            $"""
            CREATE TABLE IF NOT EXISTS {CheckpointsTable} (
                id uuid PRIMARY KEY,
                tenant_id uuid NULL,
                resource_type text NOT NULL,
                sequence bigint NOT NULL,
                head_mac bytea NOT NULL,
                created_on timestamptz NOT NULL,
                signing_key_id text NOT NULL,
                signature bytea NOT NULL,
                format_version integer NOT NULL
            );
            """,

            // A17: every index leads with the columns the queries actually filter on.
            $"CREATE INDEX IF NOT EXISTS ix_audit_records_resource ON {RecordsTable} (tenant_id, resource_type, resource_id, occurred_on, id);",
            $"CREATE INDEX IF NOT EXISTS ix_audit_records_chain_time ON {RecordsTable} (tenant_id, resource_type, occurred_on, id);",
            $"CREATE INDEX IF NOT EXISTS ix_audit_records_actor ON {RecordsTable} (tenant_id, actor_id, occurred_on, id);",
            $"CREATE INDEX IF NOT EXISTS ix_audit_records_resource_all_tenants ON {RecordsTable} (resource_type, resource_id, occurred_on, id);",
            $"CREATE INDEX IF NOT EXISTS ix_audit_records_seal_order ON {RecordsTable} (insert_xid, id);",
            $"CREATE UNIQUE INDEX IF NOT EXISTS ux_audit_records_idempotency ON {RecordsTable} (tenant_id, resource_type, idempotency_key) NULLS NOT DISTINCT WHERE idempotency_key IS NOT NULL;",
            $"CREATE UNIQUE INDEX IF NOT EXISTS ux_audit_chain_links_chain_sequence ON {LinksTable} (tenant_id, resource_type, sequence) NULLS NOT DISTINCT;",
            $"CREATE INDEX IF NOT EXISTS ix_audit_chain_links_watermark ON {LinksTable} (record_insert_xid, record_id);",
            $"CREATE INDEX IF NOT EXISTS ix_audit_chain_links_sealed_on ON {LinksTable} (sealed_on);",
            $"CREATE INDEX IF NOT EXISTS ix_audit_checkpoints_chain ON {CheckpointsTable} (tenant_id, resource_type, sequence DESC);",

            // insert_xid must be the real inserting transaction — never a caller-supplied value, or a
            // forged row could be placed behind the sealer's watermark and stay unsealed unnoticed.
            $"""
            CREATE OR REPLACE FUNCTION {StampXidFunction}() RETURNS trigger AS $stamp$
            BEGIN
                NEW.insert_xid := pg_current_xact_id()::text::bigint;
                RETURN NEW;
            END;
            $stamp$ LANGUAGE plpgsql;
            """,
            $"DROP TRIGGER IF EXISTS {StampXidTrigger} ON {RecordsTable};",
            $"CREATE TRIGGER {StampXidTrigger} BEFORE INSERT ON {RecordsTable} FOR EACH ROW EXECUTE FUNCTION {StampXidFunction}();",
            $"ALTER TABLE {RecordsTable} ENABLE ALWAYS TRIGGER {StampXidTrigger};",
        ];

        statements.AddRange(ImmutabilityTriggerSql.Statements(null, RecordsTable, allowDelete: false));
        statements.AddRange(ImmutabilityTriggerSql.Statements(null, PayloadsTable, allowDelete: true));
        statements.AddRange(ImmutabilityTriggerSql.Statements(null, LinksTable, allowDelete: false));
        statements.AddRange(ImmutabilityTriggerSql.Statements(null, CheckpointsTable, allowDelete: false));
        return statements;
    }

    private static string Join(IEnumerable<string> statements)
    {
        var builder = new StringBuilder();
        foreach (var statement in statements)
            builder.AppendLine(statement.TrimEnd()).AppendLine();
        return builder.ToString();
    }
}

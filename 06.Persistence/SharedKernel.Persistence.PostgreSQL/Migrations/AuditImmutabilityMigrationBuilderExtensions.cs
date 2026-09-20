using Microsoft.EntityFrameworkCore.Migrations;

namespace SharedKernel.Persistence.PostgreSQL.Migrations;

/// <summary>
/// <see cref="MigrationBuilder"/> extension methods generating a database-level, trigger-enforced
/// immutability guard for an append-only table (e.g. an audit-trail table).
/// </summary>
/// <remarks>
/// <para>
/// Complements — never replaces — an application-level guard (e.g. an EF Core
/// <c>SaveChanges</c> interceptor rejecting an <c>UPDATE</c>/<c>DELETE</c> against the mapped
/// entity). A raw SQL statement, a different application/service, or a human with direct database
/// access all bypass an application-level guard; a <c>BEFORE UPDATE/DELETE/TRUNCATE</c> trigger does
/// not.
/// </para>
/// <para>
/// Generic by design — usable for any append-only table, including <c>06.Persistence</c>'s own audit
/// ledger table, without this package taking a reference to that table's owning package.
/// </para>
/// </remarks>
public static class AuditImmutabilityMigrationBuilderExtensions
{
    /// <summary>
    /// Creates a <c>BEFORE UPDATE OR DELETE OR TRUNCATE</c> trigger on <paramref name="table"/> that
    /// unconditionally raises a PostgreSQL exception, making every row of the table immutable once
    /// inserted.
    /// </summary>
    /// <param name="migrationBuilder">The migration builder.</param>
    /// <param name="table">The table to protect.</param>
    /// <param name="schema">Optional schema. Defaults to the connection's search path.</param>
    /// <returns>The same <paramref name="migrationBuilder"/> for fluent chaining.</returns>
    /// <exception cref="ArgumentException"><paramref name="table"/>/<paramref name="schema"/> is not a simple identifier.</exception>
    /// <remarks>
    /// <para>
    /// Every trigger is created, then immediately switched to <c>ENABLE ALWAYS</c> — PostgreSQL's
    /// default trigger firing mode (<c>ENABLE ORIGIN</c>, what a plain <c>CREATE TRIGGER</c> leaves in
    /// place) does NOT fire while the session's <c>session_replication_role</c> is <c>replica</c>. Any
    /// session — including the application's own connection pool, which needs no special privilege to
    /// set it — can run <c>SET session_replication_role = 'replica'</c> and then freely
    /// <c>UPDATE</c>/<c>DELETE</c>/<c>TRUNCATE</c> the table, defeating an <c>ENABLE ORIGIN</c> trigger
    /// completely and silently. <c>ENABLE ALWAYS</c> fires in every replication role, closing that gap.
    /// </para>
    /// <para>
    /// A superuser or the table owner can still <c>ALTER TABLE... DISABLE TRIGGER</c> — pair this
    /// with a database-role <c>REVOKE</c> restricting who may run DDL against this table in
    /// production, which is outside what a migration-authoring helper can enforce.
    /// </para>
    /// </remarks>
    public static MigrationBuilder CreateImmutabilityTrigger(
        this MigrationBuilder migrationBuilder,
        string table,
        string? schema = null)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        var qualifiedTable = PostgresIdentifier.QualifyTable(schema, table);
        var functionName = PostgresIdentifier.QualifyTable(schema, $"{table}_reject_mutation");
        var updateTriggerName = PostgresIdentifier.Quote($"{table}_reject_update");
        var deleteTriggerName = PostgresIdentifier.Quote($"{table}_reject_delete");
        var truncateTriggerName = PostgresIdentifier.Quote($"{table}_reject_truncate");

        migrationBuilder.Sql($"""
            CREATE OR REPLACE FUNCTION {functionName}()
            RETURNS trigger AS $reject_mutation$
            BEGIN
                RAISE EXCEPTION '% is an append-only table; rows cannot be updated, deleted, or truncated', TG_TABLE_NAME;
            END;
            $reject_mutation$ LANGUAGE plpgsql;
            """);

        migrationBuilder.Sql($"""
            CREATE TRIGGER {updateTriggerName}
            BEFORE UPDATE ON {qualifiedTable}
            FOR EACH ROW EXECUTE FUNCTION {functionName}();
            """);

        migrationBuilder.Sql($"""
            CREATE TRIGGER {deleteTriggerName}
            BEFORE DELETE ON {qualifiedTable}
            FOR EACH ROW EXECUTE FUNCTION {functionName}();
            """);

        migrationBuilder.Sql($"""
            CREATE TRIGGER {truncateTriggerName}
            BEFORE TRUNCATE ON {qualifiedTable}
            FOR EACH STATEMENT EXECUTE FUNCTION {functionName}();
            """);

        // ENABLE ALWAYS — see the remarks above for why ENABLE ORIGIN (the CREATE TRIGGER default)
        // is not sufficient: any session may set session_replication_role = 'replica' and bypass it.
        migrationBuilder.Sql($"ALTER TABLE {qualifiedTable} ENABLE ALWAYS TRIGGER {updateTriggerName};");
        migrationBuilder.Sql($"ALTER TABLE {qualifiedTable} ENABLE ALWAYS TRIGGER {deleteTriggerName};");
        migrationBuilder.Sql($"ALTER TABLE {qualifiedTable} ENABLE ALWAYS TRIGGER {truncateTriggerName};");

        return migrationBuilder;
    }

    /// <summary>
    /// Drops the triggers and function <see cref="CreateImmutabilityTrigger"/> created for
    /// <paramref name="table"/> — the <c>Down</c> migration counterpart.
    /// </summary>
    /// <param name="migrationBuilder">The migration builder.</param>
    /// <param name="table">The table the triggers were created on.</param>
    /// <param name="schema">Optional schema.</param>
    /// <returns>The same <paramref name="migrationBuilder"/> for fluent chaining.</returns>
    public static MigrationBuilder DropImmutabilityTrigger(
        this MigrationBuilder migrationBuilder,
        string table,
        string? schema = null)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        var qualifiedTable = PostgresIdentifier.QualifyTable(schema, table);
        var functionName = PostgresIdentifier.QualifyTable(schema, $"{table}_reject_mutation");

        migrationBuilder.Sql($"DROP TRIGGER IF EXISTS {PostgresIdentifier.Quote($"{table}_reject_update")} ON {qualifiedTable};");
        migrationBuilder.Sql($"DROP TRIGGER IF EXISTS {PostgresIdentifier.Quote($"{table}_reject_delete")} ON {qualifiedTable};");
        migrationBuilder.Sql($"DROP TRIGGER IF EXISTS {PostgresIdentifier.Quote($"{table}_reject_truncate")} ON {qualifiedTable};");
        migrationBuilder.Sql($"DROP FUNCTION IF EXISTS {functionName}();");

        return migrationBuilder;
    }
}

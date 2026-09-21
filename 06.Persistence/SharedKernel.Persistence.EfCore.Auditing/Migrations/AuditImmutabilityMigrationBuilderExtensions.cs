using Microsoft.EntityFrameworkCore.Migrations;

namespace SharedKernel.Persistence.EfCore.Auditing.Migrations;

/// <summary>
/// <see cref="MigrationBuilder"/> extensions that create the audit ledger and make append-only tables
/// immutable at the database level.
/// </summary>
/// <remarks>
/// <para>
/// The ledger tables are not part of the EF Core model; they are created only by
/// <see cref="CreateAuditLedgerTable"/>. Add it to a hand-edited migration:
/// </para>
/// <code>
/// protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.CreateAuditLedgerTable();
/// protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropAuditLedgerTable();
/// </code>
/// <para>
/// Triggers stop every role that does not own the table (and does not disable triggers). The owner and
/// superusers can still <c>ALTER TABLE … DISABLE TRIGGER</c>, so run the application under a separate role
/// with only the grants listed in the package README; the startup self-check verifies that.
/// </para>
/// </remarks>
public static class AuditImmutabilityMigrationBuilderExtensions
{
    /// <summary>
    /// Creates the audit ledger: <c>audit_records</c>, <c>audit_record_payloads</c>,
    /// <c>audit_chain_links</c> and <c>audit_checkpoints</c>, their indexes, and their append-only
    /// triggers (<see cref="AuditLedgerSchema.CreateScript"/>). Idempotent. Requires PostgreSQL 15+.
    /// </summary>
    /// <param name="migrationBuilder">The migration builder.</param>
    /// <returns>The same <paramref name="migrationBuilder"/>.</returns>
    public static MigrationBuilder CreateAuditLedgerTable(this MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        foreach (var statement in AuditLedgerSchema.CreateStatements)
            migrationBuilder.Sql(statement);

        return migrationBuilder;
    }

    /// <summary>Drops the audit ledger created by <see cref="CreateAuditLedgerTable"/>. Destroys all audit data.</summary>
    /// <param name="migrationBuilder">The migration builder.</param>
    /// <returns>The same <paramref name="migrationBuilder"/>.</returns>
    public static MigrationBuilder DropAuditLedgerTable(this MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        foreach (var statement in AuditLedgerSchema.DropStatements)
            migrationBuilder.Sql(statement);

        return migrationBuilder;
    }

    /// <summary>
    /// Creates <c>BEFORE UPDATE</c>, <c>BEFORE DELETE</c> (unless <paramref name="allowDelete"/>) and
    /// <c>BEFORE TRUNCATE</c> triggers on <paramref name="table"/> that raise SQLSTATE 42501, switched to
    /// <c>ENABLE ALWAYS</c> so <c>session_replication_role = replica</c> does not bypass them.
    /// </summary>
    /// <param name="migrationBuilder">The migration builder.</param>
    /// <param name="table">The table to protect.</param>
    /// <param name="schema">Optional schema. Defaults to the search path.</param>
    /// <param name="allowDelete"><see langword="true"/> to leave <c>DELETE</c> allowed (an erasable table).</param>
    /// <returns>The same <paramref name="migrationBuilder"/>.</returns>
    /// <exception cref="ArgumentException"><paramref name="table"/> or <paramref name="schema"/> is not a simple identifier.</exception>
    public static MigrationBuilder CreateImmutabilityTrigger(
        this MigrationBuilder migrationBuilder,
        string table,
        string? schema = null,
        bool allowDelete = false)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        foreach (var statement in ImmutabilityTriggerSql.Statements(schema, table, allowDelete))
            migrationBuilder.Sql(statement);

        return migrationBuilder;
    }

    /// <summary>Drops the triggers and function <see cref="CreateImmutabilityTrigger"/> created.</summary>
    /// <param name="migrationBuilder">The migration builder.</param>
    /// <param name="table">The protected table.</param>
    /// <param name="schema">Optional schema.</param>
    /// <returns>The same <paramref name="migrationBuilder"/>.</returns>
    public static MigrationBuilder DropImmutabilityTrigger(
        this MigrationBuilder migrationBuilder,
        string table,
        string? schema = null)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        foreach (var statement in ImmutabilityTriggerSql.DropStatements(schema, table))
            migrationBuilder.Sql(statement);

        return migrationBuilder;
    }
}

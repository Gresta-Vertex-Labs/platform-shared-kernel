using Microsoft.EntityFrameworkCore.Migrations;
using SharedKernel.Persistence.Npgsql.RowLevelSecurity;

namespace SharedKernel.Persistence.EfCore.Migrations;

/// <summary>
/// <see cref="MigrationBuilder"/> helpers that create and drop the PostgreSQL row-level security (RLS)
/// tenant policy of a table.
/// </summary>
/// <remarks>
/// <para>
/// The policy restricts every statement — <c>SELECT</c>, <c>INSERT</c>, <c>UPDATE</c>, <c>DELETE</c>, raw
/// SQL included — to rows of the tenant bound in the current transaction:
/// <c>tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid</c>, as both <c>USING</c> and
/// <c>WITH CHECK</c>. With no tenant bound it matches nothing. The predicate is index-friendly; keep an
/// index whose leading column is the tenant column.
/// </para>
/// <para>
/// The policy binds nothing by itself and only restricts roles subject to RLS: not a superuser, not a
/// role with <c>BYPASSRLS</c>, and not the table owner unless RLS is forced (these helpers force it, but
/// an owner can still switch it off). Run migrations as an owner role and the application as a separate,
/// unprivileged role — see the <c>SharedKernel.Persistence.Npgsql</c> README for the role script.
/// </para>
/// </remarks>
public static class RowLevelSecurityMigrationBuilderExtensions
{
    private const string TenantPolicySuffix = "_tenant_isolation";
    private const string CrossTenantPolicySuffix = "_cross_tenant";

    /// <summary>
    /// Enables and forces row-level security on <paramref name="table"/> and creates its tenant policy.
    /// </summary>
    /// <param name="migrationBuilder">The migration builder.</param>
    /// <param name="table">The tenant-scoped table.</param>
    /// <param name="tenantColumn">The tenant column (a <c>uuid</c>). Defaults to <c>tenant_id</c>.</param>
    /// <param name="schema">Optional schema.</param>
    /// <param name="crossTenantRole">
    /// Optional role that may read and write every tenant's rows, through a second, role-specific policy —
    /// the alternative to giving the cross-tenant role <c>BYPASSRLS</c>. The application role must not be a
    /// member of it.
    /// </param>
    /// <returns>The same <paramref name="migrationBuilder"/>.</returns>
    public static MigrationBuilder EnableTenantRowLevelSecurity(
        this MigrationBuilder migrationBuilder,
        string table,
        string tenantColumn = "tenant_id",
        string? schema = null,
        string? crossTenantRole = null)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        var qualifiedTable = PostgresIdentifier.QualifyTable(schema, table);
        var predicate = TenantSessionSql.PolicyPredicate(PostgresIdentifier.Quote(tenantColumn));

        migrationBuilder.Sql($"ALTER TABLE {qualifiedTable} ENABLE ROW LEVEL SECURITY;");
        migrationBuilder.Sql($"ALTER TABLE {qualifiedTable} FORCE ROW LEVEL SECURITY;");
        migrationBuilder.Sql($"""
            CREATE POLICY {PolicyName(table, TenantPolicySuffix)} ON {qualifiedTable}
            USING ({predicate})
            WITH CHECK ({predicate});
            """);

        if (crossTenantRole is not null)
        {
            migrationBuilder.Sql($"""
                CREATE POLICY {PolicyName(table, CrossTenantPolicySuffix)} ON {qualifiedTable}
                TO {PostgresIdentifier.Quote(crossTenantRole)}
                USING (true)
                WITH CHECK (true);
                """);
        }

        return migrationBuilder;
    }

    /// <summary>
    /// Drops the policies <see cref="EnableTenantRowLevelSecurity"/> created and switches row-level
    /// security off on <paramref name="table"/> (<c>NO FORCE</c> and <c>DISABLE</c>) — the <c>Down</c>
    /// counterpart.
    /// </summary>
    /// <param name="migrationBuilder">The migration builder.</param>
    /// <param name="table">The table.</param>
    /// <param name="schema">Optional schema.</param>
    /// <returns>The same <paramref name="migrationBuilder"/>.</returns>
    public static MigrationBuilder DisableTenantRowLevelSecurity(
        this MigrationBuilder migrationBuilder,
        string table,
        string? schema = null)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        var qualifiedTable = PostgresIdentifier.QualifyTable(schema, table);

        migrationBuilder.Sql($"DROP POLICY IF EXISTS {PolicyName(table, CrossTenantPolicySuffix)} ON {qualifiedTable};");
        migrationBuilder.Sql($"DROP POLICY IF EXISTS {PolicyName(table, TenantPolicySuffix)} ON {qualifiedTable};");
        migrationBuilder.Sql($"ALTER TABLE {qualifiedTable} NO FORCE ROW LEVEL SECURITY;");
        migrationBuilder.Sql($"ALTER TABLE {qualifiedTable} DISABLE ROW LEVEL SECURITY;");

        return migrationBuilder;
    }

    private static string PolicyName(string table, string suffix) => PostgresIdentifier.Quote(table + suffix);
}

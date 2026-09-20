using Microsoft.EntityFrameworkCore.Migrations;

namespace SharedKernel.Persistence.PostgreSQL.Migrations;

/// <summary>
/// <see cref="MigrationBuilder"/> extension methods generating a PostgreSQL row-level security (RLS)
/// policy enforcing tenant isolation on a table, keyed off the session setting
/// <c>SharedKernel.Persistence.Npgsql</c>'s <c>ITenantSessionBinder</c> writes
/// (<c>app.tenant_id</c>).
/// </summary>
/// <remarks>
/// This is the database-level enforcement half of the tenant-safety story —
/// <c>SharedKernel.Persistence.Dapper</c>'s tenant-safe read/command services are defense-in-depth,
/// not a substitute for this policy: only a genuine RLS policy is enforced against every possible
/// SQL statement, including one this platform never wrote.
/// </remarks>
public static class RowLevelSecurityMigrationBuilderExtensions
{
    private const string DefaultPolicySuffix = "_tenant_isolation";

    /// <summary>
    /// Enables and FORCEs row-level security on <paramref name="table"/> and creates a policy
    /// restricting every row to the tenant bound by <c>set_config('app.tenant_id',...,...)</c>,
    /// with an explicit escape clause for an active cross-tenant scope.
    /// </summary>
    /// <param name="migrationBuilder">The migration builder.</param>
    /// <param name="table">The tenant-scoped table to protect.</param>
    /// <param name="tenantColumn">The table's tenant-identifier column name. Defaults to <c>tenant_id</c>.</param>
    /// <param name="schema">Optional schema.</param>
    /// <returns>The same <paramref name="migrationBuilder"/> for fluent chaining.</returns>
    /// <remarks>
    /// <para>
    /// <c>FORCE ROW LEVEL SECURITY</c> is what makes the policy apply even to the table owner — by
    /// default PostgreSQL exempts the owner from RLS, which would silently defeat this policy for
    /// whichever database role the application connects as if that role also owns the table.
    /// </para>
    /// <para>
    /// The policy's <c>USING</c> expression compares against
    /// <c>NULLIF(current_setting('app.tenant_id', true), '')::uuid</c> rather than the setting
    /// directly: when no tenant has been bound for the current session (an unset/empty setting),
    /// this evaluates to SQL <c>NULL</c>, and <c>tenant_column = NULL</c> is never true in a
    /// <c>USING</c> clause — the policy fails closed (matches zero rows) rather than raising a cast
    /// error or, worse, matching every row.
    /// </para>
    /// <para>
    /// <strong>Cross-tenant escape clause:</strong> the expression also OR's in
    /// <c>current_setting('app.cross_tenant', true) = 'on'</c> — set only by
    /// <c>ITenantSessionBinder</c> while an <c>ICrossTenantScope</c> is entered (see
    /// <c>SharedKernel.Persistence.Npgsql</c>'s <c>NpgsqlTenantSessionBinder</c> and
    /// <c>SharedKernel.Persistence.PostgreSQL</c>'s <c>RowLevelSecurityConnectionInterceptor</c>).
    /// Outside an active scope the setting is unset or explicitly <c>'off'</c>, so this half of the
    /// <c>OR</c> is never true and every row remains scoped to the bound tenant exactly as before.
    /// </para>
    /// <para>
    /// No explicit <c>WITH CHECK</c> clause is given, so PostgreSQL reuses this same <c>USING</c>
    /// expression for INSERT/UPDATE as well as SELECT/DELETE — an insert or update targeting a
    /// different tenant's row is rejected identically to a read being filtered out, and a write made
    /// while a cross-tenant scope is active is permitted identically to a read being included.
    /// </para>
    /// </remarks>
    public static MigrationBuilder EnableTenantRowLevelSecurity(
        this MigrationBuilder migrationBuilder,
        string table,
        string tenantColumn = "tenant_id",
        string? schema = null)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        var qualifiedTable = PostgresIdentifier.QualifyTable(schema, table);
        var quotedTenantColumn = PostgresIdentifier.Quote(tenantColumn);
        var policyName = PostgresIdentifier.Quote($"{table}{DefaultPolicySuffix}");

        migrationBuilder.Sql($"ALTER TABLE {qualifiedTable} ENABLE ROW LEVEL SECURITY;");
        migrationBuilder.Sql($"ALTER TABLE {qualifiedTable} FORCE ROW LEVEL SECURITY;");

        migrationBuilder.Sql($"""
            CREATE POLICY {policyName} ON {qualifiedTable}
            USING (
                {quotedTenantColumn} = NULLIF(current_setting('app.tenant_id', true), '')::uuid
                OR current_setting('app.cross_tenant', true) = 'on'
            );
            """);

        return migrationBuilder;
    }

    /// <summary>
    /// Drops the policy <see cref="EnableTenantRowLevelSecurity"/> created and disables row-level
    /// security on <paramref name="table"/> — the <c>Down</c> migration counterpart.
    /// </summary>
    /// <param name="migrationBuilder">The migration builder.</param>
    /// <param name="table">The table the policy was created on.</param>
    /// <param name="schema">Optional schema.</param>
    /// <returns>The same <paramref name="migrationBuilder"/> for fluent chaining.</returns>
    public static MigrationBuilder DisableTenantRowLevelSecurity(
        this MigrationBuilder migrationBuilder,
        string table,
        string? schema = null)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        var qualifiedTable = PostgresIdentifier.QualifyTable(schema, table);
        var policyName = PostgresIdentifier.Quote($"{table}{DefaultPolicySuffix}");

        migrationBuilder.Sql($"DROP POLICY IF EXISTS {policyName} ON {qualifiedTable};");
        migrationBuilder.Sql($"ALTER TABLE {qualifiedTable} DISABLE ROW LEVEL SECURITY;");

        return migrationBuilder;
    }
}

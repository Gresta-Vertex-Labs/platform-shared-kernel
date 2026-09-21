using Microsoft.EntityFrameworkCore.Migrations;

namespace SharedKernel.Persistence.EfCore.Migrations;

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

    private const string DefaultCrossTenantOn = "on";

    /// <summary>
    /// Enables and FORCEs row-level security on <paramref name="table"/> and creates a policy
    /// restricting every row to the tenant bound by <c>set_config('app.tenant_id',...,...)</c>,
    /// with an explicit escape clause for an active cross-tenant scope.
    /// </summary>
    /// <param name="migrationBuilder">The migration builder.</param>
    /// <param name="table">The tenant-scoped table to protect.</param>
    /// <param name="tenantColumn">The table's tenant-identifier column name. Defaults to <c>tenant_id</c>.</param>
    /// <param name="schema">Optional schema.</param>
    /// <param name="crossTenantEscapeToken">
    /// The value the policy's cross-tenant escape clause compares <c>app.cross_tenant</c> against.
    /// MUST be the exact same value passed as <c>NpgsqlPersistenceOptions.CrossTenantEscapeToken</c> at
    /// runtime — see that property's remarks for why the default, the literal <c>"on"</c>, is
    /// guessable and how to harden it. Defaults to <c>"on"</c> when omitted, matching
    /// <c>NpgsqlTenantSessionBinder</c>'s own default so an existing deployment that sets neither stays
    /// consistent without any change.
    /// </param>
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
    /// <c>current_setting('app.cross_tenant', true) = @crossTenantEscapeToken</c>, intended to be set
    /// only by <c>ITenantSessionBinder</c> while an <c>ICrossTenantScope</c> is entered (see
    /// <c>SharedKernel.Persistence.Npgsql</c>'s <c>NpgsqlTenantSessionBinder</c> and
    /// <c>SharedKernel.Persistence.EfCore</c>'s <c>RowLevelSecurityConnectionInterceptor</c>/
    /// <c>RowLevelSecurityCommandInterceptor</c>). <strong>Nothing at the database level enforces that
    /// exclusivity</strong> — <c>app.cross_tenant</c> is an ordinary session setting any statement the
    /// connecting role can execute may write, with <c>SELECT set_config('app.cross_tenant', ..., ...)</c>
    /// requiring no special privilege. With the default <paramref name="crossTenantEscapeToken"/>
    /// (<c>"on"</c>), any code path capable of running arbitrary SQL as the application's own role —
    /// not merely a caller that goes through <c>ICrossTenantScope.Enter</c> — can disable row-level
    /// security on every table this policy protects, platform-wide, with no tenant id required. Passing
    /// a long, unpublished, per-deployment secret closes that gap: an attacker with SQL execution
    /// rights but no access to this configuration value cannot guess the correct value to write.
    /// Outside an active scope the setting is unset or explicitly <c>'off'</c>, so — regardless of
    /// which token is configured — this half of the <c>OR</c> is never true for a caller that never
    /// attempts to set it, and every row remains scoped to the bound tenant exactly as before.
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
        string? schema = null,
        string crossTenantEscapeToken = DefaultCrossTenantOn)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        ArgumentException.ThrowIfNullOrWhiteSpace(crossTenantEscapeToken);

        var qualifiedTable = PostgresIdentifier.QualifyTable(schema, table);
        var quotedTenantColumn = PostgresIdentifier.Quote(tenantColumn);
        var policyName = PostgresIdentifier.Quote($"{table}{DefaultPolicySuffix}");
        var quotedEscapeToken = crossTenantEscapeToken.Replace("'", "''");

        migrationBuilder.Sql($"ALTER TABLE {qualifiedTable} ENABLE ROW LEVEL SECURITY;");
        migrationBuilder.Sql($"ALTER TABLE {qualifiedTable} FORCE ROW LEVEL SECURITY;");

        migrationBuilder.Sql($"""
            CREATE POLICY {policyName} ON {qualifiedTable}
            USING (
                {quotedTenantColumn} = NULLIF(current_setting('app.tenant_id', true), '')::uuid
                OR current_setting('app.cross_tenant', true) = '{quotedEscapeToken}'
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

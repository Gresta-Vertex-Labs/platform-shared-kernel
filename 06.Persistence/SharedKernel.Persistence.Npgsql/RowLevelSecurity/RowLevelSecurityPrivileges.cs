using System.Data.Common;

namespace SharedKernel.Persistence.Npgsql.RowLevelSecurity;

/// <summary>
/// Checks whether the role a connection runs as would silently bypass tenant row-level security.
/// </summary>
/// <remarks>
/// <para>
/// PostgreSQL skips every RLS policy for a superuser and for a role with <c>BYPASSRLS</c>, and a table's owner (or a
/// member of the owning role) can switch RLS off with <c>ALTER TABLE</c>. Policies are also permissive by default and
/// combine with <c>OR</c>: a permissive policy that does not restrict rows by the bound tenant — typically the
/// cross-tenant role's <c>TO app_cross_tenant USING (true)</c> — lets every role it applies to (that role and every
/// member of it) see all tenants. So the application role must be none of these, and a member of no role such a policy
/// targets. Membership counts whether or not it is inherited, because a member can <c>SET ROLE</c>.
/// </para>
/// <para>
/// "Protected tables" are tables with row-level security enabled and at least one policy that reads the tenant setting
/// (<see cref="TenantSessionSql.TenantIdSetting"/>). Run by <c>AddSharedKernelNpgsql</c> at startup when row-level
/// security is enabled.
/// </para>
/// </remarks>
internal static class RowLevelSecurityPrivileges
{
    private const string TenantSetting = TenantSessionSql.TenantIdSetting;

    // A policy "reads the tenant" when its USING or WITH CHECK expression mentions the tenant setting.
    private const string ReadsTenant = "position('" + TenantSetting + "' in concat(pg_get_expr({0}.polqual, {0}.polrelid), ' ', pg_get_expr({0}.polwithcheck, {0}.polrelid))) > 0";

    private static readonly string CheckSql = $"""
        SELECT r.rolname,
               r.rolsuper,
               r.rolbypassrls,
               ARRAY(
                   SELECT n.nspname || '.' || c.relname
                   FROM pg_class c
                   JOIN pg_namespace n ON n.oid = c.relnamespace
                   WHERE c.relrowsecurity AND pg_has_role(r.oid, c.relowner, 'MEMBER')
                   ORDER BY 1),
               ARRAY(
                   SELECT n.nspname || '.' || c.relname || ': permissive policy ' || p.polname || ' (to '
                          || array_to_string(ARRAY(
                                 SELECT CASE WHEN o = 0 THEN 'PUBLIC' ELSE pg_get_userbyid(o)::text END
                                 FROM unnest(p.polroles) AS o ORDER BY 1), ', ')
                          || ') does not restrict rows by tenant'
                   FROM pg_policy p
                   JOIN pg_class c ON c.oid = p.polrelid
                   JOIN pg_namespace n ON n.oid = c.relnamespace
                   WHERE c.relrowsecurity
                     AND p.polpermissive
                     AND NOT ({string.Format(System.Globalization.CultureInfo.InvariantCulture, ReadsTenant, "p")})
                     AND EXISTS (SELECT 1 FROM pg_policy t WHERE t.polrelid = c.oid AND {string.Format(System.Globalization.CultureInfo.InvariantCulture, ReadsTenant, "t")})
                     AND EXISTS (SELECT 1 FROM unnest(p.polroles) AS o WHERE o = 0 OR pg_has_role(r.oid, o, 'MEMBER'))
                   ORDER BY 1)
        FROM pg_roles r
        WHERE r.rolname = current_user
        """;

    /// <summary>Inspects the current role of <paramref name="connection"/>.</summary>
    /// <param name="connection">An open connection, authenticated as the role to check.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The findings.</returns>
    public static async Task<RowLevelSecurityPrivilegeReport> CheckAsync(
        DbConnection connection,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        await using var command = connection.CreateCommand();
        command.CommandText = CheckSql;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            throw new InvalidOperationException("The current role was not found in pg_roles.");

        return new RowLevelSecurityPrivilegeReport(
            reader.GetString(0),
            reader.GetBoolean(1),
            reader.GetBoolean(2),
            reader.GetFieldValue<string[]>(3),
            reader.GetFieldValue<string[]>(4));
    }
}

/// <summary>What <see cref="RowLevelSecurityPrivileges.CheckAsync"/> found about a role.</summary>
internal sealed class RowLevelSecurityPrivilegeReport
{
    internal RowLevelSecurityPrivilegeReport(
        string roleName,
        bool isSuperuser,
        bool bypassesRowLevelSecurity,
        IReadOnlyList<string> ownedRowLevelSecurityTables,
        IReadOnlyList<string> nonTenantPermissivePolicies)
    {
        RoleName = roleName;
        IsSuperuser = isSuperuser;
        BypassesRowLevelSecurity = bypassesRowLevelSecurity;
        OwnedRowLevelSecurityTables = ownedRowLevelSecurityTables;
        NonTenantPermissivePolicies = nonTenantPermissivePolicies;
    }

    /// <summary>The role the connection runs as.</summary>
    public string RoleName { get; }

    /// <summary>Whether the role is a superuser (bypasses every policy).</summary>
    public bool IsSuperuser { get; }

    /// <summary>Whether the role has <c>BYPASSRLS</c>.</summary>
    public bool BypassesRowLevelSecurity { get; }

    /// <summary>Tables with row-level security enabled that the role owns or can act as the owner of.</summary>
    public IReadOnlyList<string> OwnedRowLevelSecurityTables { get; }

    /// <summary>
    /// Permissive policies on protected tables that apply to the role — to <c>PUBLIC</c>, to the role itself, or to a
    /// role it is a member of — and do not restrict rows by the bound tenant, one description each.
    /// </summary>
    public IReadOnlyList<string> NonTenantPermissivePolicies { get; }

    /// <summary>Whether the role is subject to tenant row-level security and cannot switch it off or widen it.</summary>
    public bool IsSubjectToRowLevelSecurity =>
        !IsSuperuser && !BypassesRowLevelSecurity && OwnedRowLevelSecurityTables.Count == 0 && NonTenantPermissivePolicies.Count == 0;

    /// <summary>One sentence per finding, empty when <see cref="IsSubjectToRowLevelSecurity"/>.</summary>
    public IReadOnlyList<string> Problems
    {
        get
        {
            List<string> problems = [];
            if (IsSuperuser)
                problems.Add($"role '{RoleName}' is a superuser, which bypasses every row-level security policy");
            if (BypassesRowLevelSecurity)
                problems.Add($"role '{RoleName}' has BYPASSRLS, which bypasses every row-level security policy");
            if (OwnedRowLevelSecurityTables.Count > 0)
            {
                problems.Add(
                    $"role '{RoleName}' owns (or is a member of the owner of) {string.Join(", ", OwnedRowLevelSecurityTables)} "
                        + "and can disable row-level security on them; let a separate migration role own the tables");
            }

            foreach (var policy in NonTenantPermissivePolicies)
            {
                problems.Add(
                    $"role '{RoleName}' is subject to {policy}, which grants rows of every tenant (permissive policies are "
                        + "combined with OR); the role must not be a member of the cross-tenant role, which needs its own login");
            }

            return problems;
        }
    }
}

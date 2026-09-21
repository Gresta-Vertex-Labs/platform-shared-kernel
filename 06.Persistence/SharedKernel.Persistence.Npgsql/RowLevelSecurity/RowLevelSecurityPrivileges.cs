using System.Data.Common;

namespace SharedKernel.Persistence.Npgsql.RowLevelSecurity;

/// <summary>
/// Checks whether the role a connection runs as would silently bypass row-level security.
/// </summary>
/// <remarks>
/// PostgreSQL skips every RLS policy for a superuser and for a role with <c>BYPASSRLS</c>, and a table's
/// owner (or a member of the owning role) can switch RLS off with <c>ALTER TABLE</c> — so the application
/// role must be none of these. Run by <c>AddSharedKernelNpgsql</c> at startup when row-level security is
/// enabled; callable directly, e.g. from a deployment smoke test.
/// </remarks>
internal static class RowLevelSecurityPrivileges
{
    private const string CheckSql = """
        SELECT r.rolname,
               r.rolsuper,
               r.rolbypassrls,
               ARRAY(
                   SELECT n.nspname || '.' || c.relname
                   FROM pg_class c
                   JOIN pg_namespace n ON n.oid = c.relnamespace
                   WHERE c.relrowsecurity AND pg_has_role(r.oid, c.relowner, 'MEMBER')
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
            reader.GetFieldValue<string[]>(3));
    }
}

/// <summary>What <see cref="RowLevelSecurityPrivileges.CheckAsync"/> found about a role.</summary>
internal sealed class RowLevelSecurityPrivilegeReport
{
    internal RowLevelSecurityPrivilegeReport(
        string roleName,
        bool isSuperuser,
        bool bypassesRowLevelSecurity,
        IReadOnlyList<string> ownedRowLevelSecurityTables)
    {
        RoleName = roleName;
        IsSuperuser = isSuperuser;
        BypassesRowLevelSecurity = bypassesRowLevelSecurity;
        OwnedRowLevelSecurityTables = ownedRowLevelSecurityTables;
    }

    /// <summary>The role the connection runs as.</summary>
    public string RoleName { get; }

    /// <summary>Whether the role is a superuser (bypasses every policy).</summary>
    public bool IsSuperuser { get; }

    /// <summary>Whether the role has <c>BYPASSRLS</c>.</summary>
    public bool BypassesRowLevelSecurity { get; }

    /// <summary>Tables with row-level security enabled that the role owns or can act as the owner of.</summary>
    public IReadOnlyList<string> OwnedRowLevelSecurityTables { get; }

    /// <summary>Whether the role is subject to row-level security and cannot switch it off.</summary>
    public bool IsSubjectToRowLevelSecurity =>
        !IsSuperuser && !BypassesRowLevelSecurity && OwnedRowLevelSecurityTables.Count == 0;

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

            return problems;
        }
    }
}

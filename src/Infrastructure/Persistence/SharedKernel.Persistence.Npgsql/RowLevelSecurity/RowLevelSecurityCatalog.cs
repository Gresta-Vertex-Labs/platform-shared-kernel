using System.Data.Common;

namespace SharedKernel.Persistence.Npgsql.RowLevelSecurity;

/// <summary>
/// Reads, from the PostgreSQL catalog, whether tables are protected by tenant row-level security: RLS enabled and
/// forced, and at least one policy that reads the tenant setting.
/// </summary>
/// <remarks>
/// For a model-driven startup check (every tenant-scoped table must be protected). <c>SharedKernel.Persistence.EfCore</c>
/// reaches it through <c>InternalsVisibleTo</c>; it is not part of the public surface.
/// </remarks>
internal static class RowLevelSecurityCatalog
{
    private const string StatusSql = $"""
        SELECT t.name,
               c.oid IS NOT NULL,
               COALESCE(c.relrowsecurity, false),
               COALESCE(c.relforcerowsecurity, false),
               EXISTS (
                   SELECT 1 FROM pg_policy p
                   WHERE p.polrelid = c.oid
                     AND position('{TenantSessionSql.TenantIdSetting}' in concat(pg_get_expr(p.polqual, p.polrelid), ' ', pg_get_expr(p.polwithcheck, p.polrelid))) > 0)
        FROM unnest(@tables) WITH ORDINALITY AS t(name, ordinal)
        LEFT JOIN pg_class c ON c.oid = to_regclass(t.name)
        ORDER BY t.ordinal
        """;

    /// <summary>Reads the row-level security status of <paramref name="tables"/>.</summary>
    /// <param name="connection">An open connection.</param>
    /// <param name="tables">
    /// Table names as <c>to_regclass</c> accepts them: <c>"orders"</c>, <c>"sales.orders"</c>, or quoted identifiers
    /// (<c>"\"Orders\""</c>). Unqualified names resolve through the connection's search path.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>One status per table, in the order given.</returns>
    public static async Task<IReadOnlyList<RowLevelSecurityTableStatus>> GetTableStatusAsync(
        DbConnection connection,
        IReadOnlyList<string> tables,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(tables);
        if (tables.Count == 0)
            return [];

        await using var command = connection.CreateCommand();
        command.CommandText = StatusSql;
        var parameter = command.CreateParameter();
        parameter.ParameterName = "tables";
        parameter.Value = tables.ToArray();
        command.Parameters.Add(parameter);

        var result = new List<RowLevelSecurityTableStatus>(tables.Count);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            result.Add(new RowLevelSecurityTableStatus(
                reader.GetString(0), reader.GetBoolean(1), reader.GetBoolean(2), reader.GetBoolean(3), reader.GetBoolean(4)));
        }

        return result;
    }
}

/// <summary>The row-level security status of one table.</summary>
/// <param name="Table">The table name as given.</param>
/// <param name="Exists">Whether the table exists.</param>
/// <param name="RowSecurityEnabled"><c>ENABLE ROW LEVEL SECURITY</c> (<c>relrowsecurity</c>).</param>
/// <param name="RowSecurityForced"><c>FORCE ROW LEVEL SECURITY</c> (<c>relforcerowsecurity</c>): applies to the owner too.</param>
/// <param name="HasTenantPolicy">At least one policy reads the tenant setting.</param>
internal sealed record RowLevelSecurityTableStatus(
    string Table, bool Exists, bool RowSecurityEnabled, bool RowSecurityForced, bool HasTenantPolicy)
{
    /// <summary>Whether the table is protected: it exists, RLS is enabled and forced, and a tenant policy exists.</summary>
    public bool IsProtected => Exists && RowSecurityEnabled && RowSecurityForced && HasTenantPolicy;
}

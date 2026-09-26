using SharedKernel.Execution.Tenancy;

namespace SharedKernel.Persistence.Npgsql.RowLevelSecurity;

/// <summary>
/// The SQL contract between tenant row-level security policies and the code that binds the tenant: one
/// transaction-local setting, <see cref="TenantIdSetting"/>, read by one policy predicate.
/// </summary>
/// <remarks>
/// <para>
/// The policy predicate is <c>tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid</c>
/// (see <see cref="PolicyPredicate"/>). An unset or empty setting makes it <c>NULL</c>, which matches no
/// row, so a caller that bound no tenant sees nothing and can write nothing. <c>current_setting</c> is
/// stable within a statement, so the predicate can use an index on the tenant column.
/// </para>
/// <para>
/// The setting is only ever written transaction-locally (<c>set_config(..., true)</c>), so it never
/// outlives the transaction that wrote it — the property that makes the binding safe behind a
/// transaction-mode connection pooler such as PgBouncer.
/// </para>
/// </remarks>
internal static class TenantSessionSql
{
    /// <summary>The custom setting holding the bound tenant id (empty when none is bound).</summary>
    public const string TenantIdSetting = "app.tenant_id";

    /// <summary>
    /// A statement that binds <paramref name="tenantId"/> transaction-locally and returns no result set,
    /// so it can precede another statement in the same command without changing what that command returns.
    /// </summary>
    /// <param name="tenantId">The tenant to bind, or <see langword="null"/> for none.</param>
    /// <returns>A <c>DO</c> block ending with a semicolon.</returns>
    /// <remarks>
    /// <para>
    /// The tenant id is inlined because a <c>DO</c> block takes no parameters; a <see cref="TenantId"/> formats as a
    /// lowercase <c>D</c>-format Guid, which contains only hexadecimal digits and hyphens, so nothing can be injected.
    /// </para>
    /// <para>
    /// Outside an explicit transaction, the statements of one command run in a single implicit
    /// transaction (one Sync of the extended protocol), so the binding applies to the statement it
    /// precedes and disappears when that command completes.
    /// </para>
    /// </remarks>
    public static string BindStatement(TenantId? tenantId)
    {
        var value = tenantId?.ToString() ?? string.Empty;
        return $"DO $sk_rls$BEGIN PERFORM set_config('{TenantIdSetting}', '{value}', true); END$sk_rls$;";
    }

    /// <summary>
    /// The row-level security predicate that restricts <paramref name="quotedTenantColumn"/> to the bound
    /// tenant.
    /// </summary>
    /// <param name="quotedTenantColumn">The tenant column, already quoted as an SQL identifier.</param>
    /// <returns>The predicate, for both <c>USING</c> and <c>WITH CHECK</c>.</returns>
    public static string PolicyPredicate(string quotedTenantColumn)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(quotedTenantColumn);
        return $"{quotedTenantColumn} = NULLIF(current_setting('{TenantIdSetting}', true), '')::uuid";
    }
}

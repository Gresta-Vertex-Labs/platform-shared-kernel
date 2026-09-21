namespace SharedKernel.Persistence.Npgsql.Connections;

/// <summary>
/// DI service keys of the secondary data sources and connection factories
/// <c>AddSharedKernelNpgsql</c> registers next to the default (unkeyed) ones.
/// </summary>
/// <remarks>
/// Resolve with <c>GetRequiredKeyedService&lt;IDbConnectionFactory&gt;(NpgsqlDataSourceKeys.ReadOnly)</c> or
/// <c>[FromKeyedServices(NpgsqlDataSourceKeys.ReadOnly)]</c>.
/// </remarks>
public static class NpgsqlDataSourceKeys
{
    /// <summary>
    /// Read-only traffic. The keyed <c>IDbConnectionFactory</c> is always registered: it uses
    /// <c>ReadOnlyConnectionString</c> when configured, otherwise a standby of a multi-host connection
    /// string (<c>TargetSessionAttributes=PreferStandby</c>), otherwise the primary.
    /// </summary>
    public const string ReadOnly = "SharedKernel.Persistence.Npgsql.ReadOnly";

    /// <summary>
    /// The cross-tenant database role used while an <c>ICrossTenantScope</c> is active and row-level
    /// security is on. The keyed <c>NpgsqlDataSource</c> and <c>IDbConnectionFactory</c> are registered
    /// only when <c>RowLevelSecurity:CrossTenantConnectionString</c> is configured.
    /// </summary>
    public const string CrossTenant = "SharedKernel.Persistence.Npgsql.CrossTenant";

    /// <summary>
    /// Direct (non-pooler) connections for migrations and session-level advisory locks. The keyed
    /// <c>NpgsqlDataSource</c> is registered only when <c>MigrationConnectionString</c> is configured;
    /// otherwise migration work uses the default data source.
    /// </summary>
    public const string Migration = "SharedKernel.Persistence.Npgsql.Migration";
}

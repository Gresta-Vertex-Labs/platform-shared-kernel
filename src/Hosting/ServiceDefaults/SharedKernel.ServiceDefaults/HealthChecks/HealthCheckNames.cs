namespace SharedKernel.ServiceDefaults.HealthChecks;

/// <summary>
/// Well-known default health-check <em>registration names</em> used throughout this domain.
/// </summary>
/// <remarks>
/// <para>
/// Mirrors the <see cref="HealthCheckTags"/> constants-class pattern. Every <c>Add*Check</c> extension method's
/// default <c>name</c> parameter, and the inline <c>"startup"</c> registration inside
/// <see cref="HealthCheckExtensions.AddSharedKernelHealthChecks"/>, reference these constants. Callers may still pass
/// a custom <c>name</c> at the call site; these constants only define the defaults.
/// </para>
/// <para>
/// Checks mapped from provider readiness probes by
/// <see cref="ReadinessHealthCheckExtensions.AddSharedKernelReadiness"/> are named after the probe, and their names
/// are declared by the provider package that registers the probe (for example <c>RedisReadinessProbeNames</c> or
/// <c>StorageReadinessProbeNames</c>), not here.
/// </para>
/// </remarks>
public static class HealthCheckNames
{
    /// <summary>Default registration name for the EF Core database readiness check (<c>AddDatabaseReadinessCheck&lt;TContext&gt;</c>).</summary>
    public const string Database = "database";

    /// <summary>Default registration name for the connection-factory database readiness check (<c>AddDapperDatabaseReadinessCheck</c>).</summary>
    public const string DapperDatabase = "database-dapper";

    /// <summary>Default registration name for the startup-migration readiness check (<c>AddPersistenceStartupReadinessCheck</c>).</summary>
    public const string PersistenceStartup = "persistence-startup";

    /// <summary>Registration name for the always-on <see cref="Probes.StartupGateHealthCheck"/>.</summary>
    public const string Startup = "startup";
}

namespace SharedKernel.ServiceDefaults.HealthChecks;

/// <summary>
/// Well-known default health-check <em>registration names</em> used throughout this domain.
/// </summary>
/// <remarks>
/// Mirrors the <see cref="HealthCheckTags"/> constants-class pattern. Every <c>Add*HealthCheck</c>
/// extension method's default <c>name</c> parameter, and the inline <c>"startup"</c> registration
/// inside <see cref="HealthCheckExtensions.AddSharedKernelHealthChecks"/>, reference these
/// constants — zero bare-literal health-check registration names remain anywhere in
/// <c>SharedKernel.ServiceDefaults</c>. Callers may still pass a custom <c>name</c> at the call
/// site; these constants only define the defaults.
/// </remarks>
public static class HealthCheckNames
{
    /// <summary>Default registration name for database readiness checks.</summary>
    public const string Database = "database";

    /// <summary>Default registration name for Redis connectivity checks.</summary>
    public const string Redis = "redis";

    /// <summary>Default registration name for message-bus connectivity checks.</summary>
    public const string Messaging = "messaging";

    /// <summary>Default registration name for cache readiness checks.</summary>
    public const string Cache = "cache";

    /// <summary>Registration name for the always-on <see cref="Probes.StartupGateHealthCheck"/>.</summary>
    public const string Startup = "startup";

    /// <summary>Default registration name for object-storage connectivity checks.</summary>
    public const string Storage = "storage";

    /// <summary>Default registration name for search-index connectivity checks.</summary>
    public const string Search = "search";

    /// <summary>Default registration name for vector-store connectivity checks.</summary>
    public const string VectorStore = "vector-store";

    /// <summary>Default registration name for workflow-service connectivity checks.</summary>
    public const string Workflows = "workflows";
}

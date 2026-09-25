namespace SharedKernel.ServiceDefaults.HealthChecks;

/// <summary>
/// Well-known health check tag names used throughout this domain.
/// </summary>
/// <remarks>
/// <see cref="Live"/> and <see cref="Ready"/> are the two endpoint-selecting tags. <see cref="Db"/> is a
/// dependency-category tag applied alongside <see cref="Ready"/>, never alongside <see cref="Live"/>. Checks mapped
/// from provider readiness probes carry <see cref="Ready"/> only; the probe name already identifies the dependency.
/// </remarks>
public static class HealthCheckTags
{
    /// <summary>
    /// Tags a check as eligible for <c>/health/live</c>. Must only be used for process-alive
    /// signals — never for checks that depend on an external system.
    /// </summary>
    public const string Live = "live";

    /// <summary>
    /// Tags a check as eligible for <c>/health/ready</c>. Used for checks that gate
    /// load-balancer routing, including all dependency-specific checks.
    /// </summary>
    public const string Ready = "ready";

    /// <summary>Dependency-category tag for database readiness checks.</summary>
    public const string Db = "db";
}

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace SharedKernel.ServiceDefaults.HealthChecks;

/// <summary>
/// Opt-in cache readiness health check, probing the registered <c>ICacheService</c> directly.
/// </summary>
public static class CacheReadinessHealthCheckExtensions
{
    /// <summary>
    /// Registers a health check that probes <c>ICacheService.GetAsync&lt;string&gt;</c> with a
    /// synthetic key and a short timeout.
    /// </summary>
    /// <param name="builder">The health checks builder.</param>
    /// <param name="name">The health check registration name. Defaults to <see cref="HealthCheckNames.Cache"/>.</param>
    /// <returns>The same <paramref name="builder"/> instance, for fluent chaining.</returns>
    /// <remarks>
    /// Tagged <see cref="HealthCheckTags.Ready"/> and <see cref="HealthCheckTags.Cache"/>. Reports
    /// <see cref="HealthStatus.Degraded"/> — never <see cref="HealthStatus.Unhealthy"/> — on probe
    /// failure (see <see cref="CacheReadinessHealthCheck"/>). Opt-in only.
    /// </remarks>
    public static IHealthChecksBuilder AddCacheReadinessCheck(
        this IHealthChecksBuilder builder,
        string name = HealthCheckNames.Cache)
    {
        ArgumentNullException.ThrowIfNull(builder);

        string[] tags = [HealthCheckTags.Ready, HealthCheckTags.Cache];

        HealthCheckRegistrationLogging.LogRegistration(
            builder.Services,
            "SharedKernel.ServiceDefaults.HealthChecks.CacheReadinessHealthCheckExtensions",
            name,
            tags);

        return builder.AddCheck<CacheReadinessHealthCheck>(
            name,
            tags: tags);
    }
}

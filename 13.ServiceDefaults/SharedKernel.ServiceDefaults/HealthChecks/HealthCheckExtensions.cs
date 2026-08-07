using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using SharedKernel.ServiceDefaults.Probes;

namespace SharedKernel.ServiceDefaults.HealthChecks;

/// <summary>
/// Registers the base health check infrastructure shared by every Platform.SharedKernel
/// microservice: the always-on <see cref="StartupGateHealthCheck"/> and the
/// <c>/health/live</c> / <c>/health/ready</c> endpoint mappings.
/// </summary>
public static class HealthCheckExtensions
{
    /// <summary>
    /// Registers <see cref="StartupGate"/> and <see cref="StartupGateHealthCheck"/> and returns
    /// the <see cref="IHealthChecksBuilder"/> so callers can chain opt-in, dependency-specific
    /// health checks (e.g. <c>AddRedisHealthCheck</c>, <c>AddDatabaseReadinessCheck</c>).
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>
    /// The <see cref="IHealthChecksBuilder"/> for chaining additional, opt-in health checks.
    /// </returns>
    /// <remarks>
    /// Registers <b>only</b> base infrastructure — never a dependency-specific check. Every
    /// dependency-specific check (database, Redis, messaging, cache) is an explicit opt-in call
    /// on the returned builder.
    /// </remarks>
    public static IHealthChecksBuilder AddSharedKernelHealthChecks(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<StartupGate>();

        return services
            .AddHealthChecks()
            .AddCheck<StartupGateHealthCheck>(
                HealthCheckNames.Startup,
                tags: [HealthCheckTags.Ready]);
    }

    /// <summary>
    /// Maps the <c>/health/live</c> and <c>/health/ready</c> endpoints with the hard
    /// liveness/readiness tag split: <c>/health/live</c> evaluates only checks tagged
    /// <see cref="HealthCheckTags.Live"/> (process-alive signal only — never dependency-coupled);
    /// <c>/health/ready</c> evaluates only checks tagged <see cref="HealthCheckTags.Ready"/>
    /// (may depend on database, cache, or broker connectivity).
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <returns>The same <paramref name="endpoints"/> instance, for fluent chaining.</returns>
    public static IEndpointRouteBuilder MapDefaultHealthCheckEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains(HealthCheckTags.Live),
        });

        endpoints.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains(HealthCheckTags.Ready),
        });

        return endpoints;
    }
}

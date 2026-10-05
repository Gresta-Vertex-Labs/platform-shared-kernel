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
    /// health checks (e.g. <see cref="ReadinessHealthCheckExtensions.AddSharedKernelReadiness"/>, <c>AddDatabaseReadinessCheck</c>).
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>
    /// The <see cref="IHealthChecksBuilder"/> for chaining additional, opt-in health checks.
    /// </returns>
    /// <remarks>
    /// Registers <b>only</b> base infrastructure — never a dependency-specific check. Every
    /// dependency-specific check — every provider readiness probe through <c>AddSharedKernelReadiness()</c>, the
    /// database checks of <c>SharedKernel.ServiceDefaults.Persistence</c> — is an explicit opt-in call
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
    /// <param name="requireAuthorization">
    /// When <see langword="true"/>, both endpoint mappings chain <c>.RequireAuthorization()</c>.
    /// Defaults to <see langword="false"/> — byte-identical to this method's pre-P-399 behavior.
    /// </param>
    /// <returns>The same <paramref name="endpoints"/> instance, for fluent chaining.</returns>
    /// <remarks>
    /// <b>DEFENSE-IN-DEPTH ONLY, NEVER A SUBSTITUTE FOR NETWORK ISOLATION.</b>
    /// <c>/health/live</c>/<c>/health/ready</c> MUST BE NETWORK-RESTRICTED AT THE
    /// INGRESS/<c>NETWORKPOLICY</c> LAYER IN ANY ENVIRONMENT WHERE THEY ARE NOT INTENTIONALLY
    /// PUBLIC, INDEPENDENT OF WHETHER <paramref name="requireAuthorization"/> IS USED. A Kubernetes
    /// kubelet's own liveness/readiness probe calls are typically unauthenticated — enabling this
    /// parameter on an endpoint set the kubelet itself calls will cause the kubelet's own probes to
    /// be rejected. When exposing health data to an external audience, prefer a minimal response
    /// writer that serializes only <c>{ status }</c> — never <c>HealthReport.Entries[*].Data</c>/
    /// <c>.Description</c>, which can leak dependency version/connection details.
    /// </remarks>
    public static IEndpointRouteBuilder MapDefaultHealthCheckEndpoints(
        this IEndpointRouteBuilder endpoints,
        bool requireAuthorization = false)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var liveBuilder = endpoints.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains(HealthCheckTags.Live),
        });

        var readyBuilder = endpoints.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains(HealthCheckTags.Ready),
        });

        if (requireAuthorization)
        {
            liveBuilder.RequireAuthorization();
            readyBuilder.RequireAuthorization();
        }

        return endpoints;
    }
}

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using SharedKernel.Caching.Redis.Core.Health;

namespace SharedKernel.ServiceDefaults.HealthChecks;

/// <summary>
/// Opt-in Redis readiness health check over the shared connection registered by <c>AddRedisConnection</c>.
/// </summary>
public static class RedisHealthCheckExtensions
{
    /// <summary>
    /// Registers a health check that sends a <c>PING</c> over the shared Redis connection, through the
    /// <see cref="IRedisConnectionProbe"/> resolved from DI.
    /// </summary>
    /// <param name="builder">The health checks builder.</param>
    /// <param name="name">The health check registration name. Defaults to <see cref="HealthCheckNames.Redis"/>.</param>
    /// <returns>The same <paramref name="builder"/> instance, for fluent chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// Tagged <see cref="HealthCheckTags.Ready"/>, <see cref="HealthCheckTags.Redis"/>, and
    /// <see cref="HealthCheckTags.Cache"/>, never <see cref="HealthCheckTags.Live"/>: an unreachable Redis takes
    /// the pod out of load-balancer rotation but never makes Kubernetes restart it. Reports
    /// <see cref="HealthStatus.Unhealthy"/> when Redis does not answer; the description never contains the
    /// connection string or an exception message.
    /// </para>
    /// <para>
    /// Requires <c>services.AddRedisConnection(...)</c> (<c>SharedKernel.Caching.Redis.Core</c>), which
    /// registers <see cref="IRedisConnectionProbe"/>; without it the check fails when first run. The check opens
    /// no connection of its own, so TLS, timeouts and credentials are those of the shared connection. Opt-in
    /// only — never registered by <c>AddServiceDefaults()</c> or <c>AddSharedKernelHealthChecks()</c>; a
    /// service that caches only in memory must not register it.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// builder.Services.AddRedisConnection(builder.Configuration);
    /// builder.Services.AddHealthChecks().AddRedisHealthCheck();
    /// </code>
    /// </example>
    public static IHealthChecksBuilder AddRedisHealthCheck(
        this IHealthChecksBuilder builder,
        string name = HealthCheckNames.Redis)
    {
        ArgumentNullException.ThrowIfNull(builder);

        string[] tags = [HealthCheckTags.Ready, HealthCheckTags.Redis, HealthCheckTags.Cache];

        HealthCheckRegistrationLogging.LogRegistration(
            builder.Services,
            "SharedKernel.ServiceDefaults.HealthChecks.RedisHealthCheckExtensions",
            name,
            tags);

        return builder.Add(new HealthCheckRegistration(
            name,
            sp => new RedisConnectionReadinessHealthCheck(sp.GetRequiredService<IRedisConnectionProbe>()),
            failureStatus: null,
            tags: tags));
    }
}

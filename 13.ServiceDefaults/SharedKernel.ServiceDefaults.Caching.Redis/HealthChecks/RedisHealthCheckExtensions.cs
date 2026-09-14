using HealthChecks.Redis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace SharedKernel.ServiceDefaults.HealthChecks;

/// <summary>
/// Opt-in Redis connectivity health check, wrapping <c>AspNetCore.HealthChecks.Redis</c>.
/// </summary>
public static class RedisHealthCheckExtensions
{
    /// <summary>
    /// Registers a health check that verifies <c>StackExchange.Redis</c> connectivity against
    /// <paramref name="connectionString"/>.
    /// </summary>
    /// <param name="builder">The health checks builder.</param>
    /// <param name="connectionString">The Redis connection string to probe.</param>
    /// <param name="name">The health check registration name. Defaults to <see cref="HealthCheckNames.Redis"/>.</param>
    /// <returns>The same <paramref name="builder"/> instance, for fluent chaining.</returns>
    /// <remarks>
    /// Tagged <see cref="HealthCheckTags.Ready"/>, <see cref="HealthCheckTags.Redis"/>, and
    /// <see cref="HealthCheckTags.Cache"/>. Opt-in only — a service that uses only the L1
    /// in-process cache must never register this check and must never fail readiness because no
    /// Redis is configured. Never registered by <c>AddServiceDefaults()</c> or
    /// <c>AddSharedKernelHealthChecks()</c>.
    /// </remarks>
    public static IHealthChecksBuilder AddRedisHealthCheck(
        this IHealthChecksBuilder builder,
        string connectionString,
        string name = HealthCheckNames.Redis)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        string[] tags = [HealthCheckTags.Ready, HealthCheckTags.Redis, HealthCheckTags.Cache];

        HealthCheckRegistrationLogging.LogRegistration(
            builder.Services,
            "SharedKernel.ServiceDefaults.HealthChecks.RedisHealthCheckExtensions",
            name,
            tags);

        return builder.AddRedis(
            connectionString,
            name: name,
            tags: tags);
    }
}

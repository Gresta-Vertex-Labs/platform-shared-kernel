using Microsoft.Extensions.Diagnostics.HealthChecks;
using SharedKernel.Caching.Redis.Core.Health;

namespace SharedKernel.ServiceDefaults.HealthChecks;

/// <summary>
/// Wraps <see cref="IRedisConnectionProbe.ProbeAsync"/> (<c>02.Caching/SharedKernel.Caching.Redis.Core</c>)
/// in an <see cref="IHealthCheck"/>.
/// </summary>
/// <remarks>
/// <para>
/// The probe sends a <c>PING</c> over the one connection every Redis package shares, so readiness reflects
/// what the distributed cache, locks, hash store and pub/sub actually see, and the check never opens a
/// connection of its own.
/// </para>
/// <para>
/// Reports <see cref="HealthStatus.Healthy"/> with the round-trip time in <see cref="HealthCheckResult.Data"/>
/// (<c>latency</c>) when Redis answers, and <see cref="HealthStatus.Unhealthy"/> with the probe's
/// <see cref="RedisConnectionHealth.Description"/> otherwise — never <see cref="HealthStatus.Degraded"/>,
/// matching the other raw-connectivity checks (<c>DatabaseReadinessHealthCheck</c>,
/// <c>StorageReadinessHealthCheck</c>). The probe reports an unreachable server rather than throwing; any
/// other failure is reported as <see cref="HealthStatus.Unhealthy"/> naming only the exception type, because
/// health endpoints may be exposed and an exception message can carry an endpoint or credential detail.
/// Cancellation propagates.
/// </para>
/// </remarks>
internal sealed class RedisConnectionReadinessHealthCheck(IRedisConnectionProbe probe) : IHealthCheck
{
    /// <inheritdoc/>
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        RedisConnectionHealth health;
        try
        {
            health = await probe.ProbeAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy($"Redis readiness probe failed ({ex.GetType().Name}).");
        }

        if (!health.IsHealthy)
            return HealthCheckResult.Unhealthy(health.Description ?? "Redis is not reachable.");

        var data = new Dictionary<string, object>();
        if (health.Latency is { } latency)
            data["latency"] = latency;

        return HealthCheckResult.Healthy("Redis answered PING.", data);
    }
}

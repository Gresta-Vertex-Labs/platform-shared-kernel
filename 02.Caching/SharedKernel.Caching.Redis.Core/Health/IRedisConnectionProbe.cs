namespace SharedKernel.Caching.Redis.Core.Health;

/// <summary>A readiness check for the shared Redis connection.</summary>
/// <remarks>
/// <para>
/// Registered by <c>AddRedisConnection</c>. It checks the connection every Redis package uses, so it reports
/// what the cache, locks, hash store and pub/sub actually see, without opening a connection of its own.
/// </para>
/// <para>
/// An unreachable server is reported as unhealthy, never thrown. This package ships no <c>IHealthCheck</c>;
/// <c>SharedKernel.ServiceDefaults.Caching.Redis</c> wires the probe into health checks.
/// </para>
/// </remarks>
public interface IRedisConnectionProbe
{
    /// <summary>Sends a <c>PING</c> over the shared connection.</summary>
    /// <param name="cancellationToken">A token to cancel the check.</param>
    /// <returns>The result.</returns>
    Task<RedisConnectionHealth> ProbeAsync(CancellationToken cancellationToken = default);
}

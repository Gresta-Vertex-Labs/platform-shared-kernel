using Microsoft.Extensions.Diagnostics.HealthChecks;
using SharedKernel.Caching.Abstractions;

namespace SharedKernel.ServiceDefaults.HealthChecks;

/// <summary>
/// Probes <see cref="ICacheService"/> with a synthetic key and a short timeout. Reports
/// <see cref="HealthStatus.Degraded"/> — never <see cref="HealthStatus.Unhealthy"/> — when the
/// probe fails.
/// </summary>
/// <remarks>
/// FusionCache's L1 in-process fail-safe may still be serving stale data correctly even when L2
/// (Redis) is unavailable; a hard <see cref="HealthStatus.Unhealthy"/> would unnecessarily remove
/// the pod from load-balancer rotation during a transient cache blip. Tagged
/// <see cref="HealthCheckTags.Ready"/> and <see cref="HealthCheckTags.Cache"/>.
/// </remarks>
internal sealed class CacheReadinessHealthCheck(ICacheService cacheService) : IHealthCheck
{
    private const string ProbeKey = "__sharedkernel_health_probe__";
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(2);

    /// <inheritdoc/>
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var timeoutCts = new CancellationTokenSource(ProbeTimeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            await cacheService.TryGetAsync<string>(ProbeKey, linkedCts.Token).ConfigureAwait(false);

            return HealthCheckResult.Healthy("Cache probe succeeded.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Degraded("Cache probe failed; fail-safe may still serve stale data.", ex);
        }
    }
}

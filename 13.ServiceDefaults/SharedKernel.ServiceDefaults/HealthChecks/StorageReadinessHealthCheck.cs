using Microsoft.Extensions.Diagnostics.HealthChecks;
using SharedKernel.Storage.Abstractions.Abstractions;

namespace SharedKernel.ServiceDefaults.HealthChecks;

/// <summary>
/// Wraps <see cref="IFileStorage.CheckHealthAsync"/> (<c>08.Storage</c>) in an <see cref="IHealthCheck"/>.
/// </summary>
/// <remarks>
/// Reports <see cref="HealthStatus.Healthy"/> when the probe's <c>Result.IsSuccess</c> is
/// <see langword="true"/>; <see cref="HealthStatus.Unhealthy"/> otherwise — calibrated like
/// <see cref="RedisHealthCheckExtensions.AddRedisHealthCheck"/> and
/// <see cref="DatabaseReadinessHealthCheck{TContext}"/>, both <see cref="HealthStatus.Unhealthy"/>
/// on failure, because no fail-safe/graceful-degradation layer sits in front of raw object-storage
/// connectivity (unlike <see cref="CacheReadinessHealthCheck"/>'s FusionCache-L1-absorption
/// rationale for <see cref="HealthStatus.Degraded"/>). The failing error's message is surfaced via
/// <see cref="HealthCheckResult.Description"/>. <c>08.Storage</c> ships only the probe primitive —
/// this adapter is the <c>13.ServiceDefaults</c>-owned <see cref="IHealthCheck"/> wiring per the
/// platform's "OTel/health check/probe wiring lives in 13.ServiceDefaults" rule.
/// </remarks>
internal sealed class StorageReadinessHealthCheck(IFileStorage fileStorage, string bucket) : IHealthCheck
{
    /// <inheritdoc/>
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var result = await fileStorage.CheckHealthAsync(bucket, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess
            ? HealthCheckResult.Healthy("Storage reachable.")
            : HealthCheckResult.Unhealthy(result.Error.Message);
    }
}

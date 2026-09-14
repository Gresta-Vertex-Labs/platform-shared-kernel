using Microsoft.Extensions.Diagnostics.HealthChecks;
using SharedKernel.AI.Abstractions.Abstractions;

namespace SharedKernel.ServiceDefaults.HealthChecks;

/// <summary>
/// Wraps <see cref="IVectorCollectionProvisioner.ProbeAsync"/> (<c>10.Intelligence</c>) in an
/// <see cref="IHealthCheck"/>.
/// </summary>
/// <remarks>
/// Reports <see cref="HealthStatus.Healthy"/> when the probe's <c>Result.IsSuccess</c> is
/// <see langword="true"/> and its <c>VectorCollectionHealth.Reachable</c>,
/// <c>.CollectionAddressable</c>, and <c>.Queryable</c> members are all <see langword="true"/>;
/// <see cref="HealthStatus.Unhealthy"/> otherwise — calibrated like
/// <c>SearchReadinessHealthCheck</c> and <c>StorageReadinessHealthCheck</c>, never
/// <see cref="HealthStatus.Degraded"/>, because no fail-safe/graceful-degradation layer sits in
/// front of raw vector-store connectivity (unlike <c>CacheReadinessHealthCheck</c>'s
/// FusionCache-L1-absorption rationale). <c>VectorCount</c>, <c>PendingWriteCount</c>,
/// <c>EngineVersion</c>, <c>SchemaFingerprint</c>, and <c>Latency</c> are surfaced via
/// <see cref="HealthCheckResult.Data"/> as informational values only — <c>PendingWriteCount</c> is
/// <b>never</b> factored into the Healthy/Unhealthy decision, per <c>10.Intelligence/CLAUDE.md</c>'s
/// own explicit rule that a deep write backlog means results may be stale, not unavailable, and
/// failing readiness would remove serving capacity exactly when it is most needed.
/// <c>10.Intelligence</c> ships only the probe primitive — this adapter is the
/// <c>13.ServiceDefaults</c>-owned <see cref="IHealthCheck"/> wiring per the platform's
/// "OTel/health check/probe wiring lives in 13.ServiceDefaults" rule.
/// </remarks>
internal sealed class VectorStoreReadinessHealthCheck(
    IVectorCollectionProvisioner provisioner,
    string collectionName) : IHealthCheck
{
    /// <inheritdoc/>
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var result = await provisioner.ProbeAsync(collectionName, cancellationToken).ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            return HealthCheckResult.Unhealthy(result.Error.Message);
        }

        var health = result.Value;
        var data = new Dictionary<string, object>
        {
            ["vectorCount"] = health.VectorCount,
            ["pendingWriteCount"] = health.PendingWriteCount.HasValue
                ? health.PendingWriteCount.Value
                : "n/a",
            ["engineVersion"] = health.EngineVersion,
            ["schemaFingerprint"] = health.SchemaFingerprint ?? "n/a",
            ["latency"] = health.Latency,
        };

        return health is { Reachable: true, CollectionAddressable: true, Queryable: true }
            ? HealthCheckResult.Healthy("Vector collection reachable, addressable, and queryable.", data)
            : HealthCheckResult.Unhealthy(
                $"Vector collection '{collectionName}' not ready (Reachable={health.Reachable}, " +
                $"CollectionAddressable={health.CollectionAddressable}, Queryable={health.Queryable}).",
                data: data);
    }
}

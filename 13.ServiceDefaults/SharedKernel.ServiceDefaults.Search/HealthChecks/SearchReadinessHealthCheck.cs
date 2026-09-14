using Microsoft.Extensions.Diagnostics.HealthChecks;
using SharedKernel.Search.Abstractions.Abstractions;

namespace SharedKernel.ServiceDefaults.HealthChecks;

/// <summary>
/// Wraps <see cref="ISearchIndexProvisioner.ProbeAsync"/> (<c>09.Search</c>) in an
/// <see cref="IHealthCheck"/>.
/// </summary>
/// <remarks>
/// Reports <see cref="HealthStatus.Healthy"/> when the probe's <c>Result.IsSuccess</c> is
/// <see langword="true"/> and its <c>SearchIndexHealth.Reachable</c>,
/// <c>.IndexAddressable</c>, and <c>.Searchable</c> members are all <see langword="true"/>;
/// <see cref="HealthStatus.Unhealthy"/> otherwise — calibrated like
/// <c>StorageReadinessHealthCheck</c> and <c>DatabaseReadinessHealthCheck&lt;TContext&gt;</c>,
/// never <see cref="HealthStatus.Degraded"/>, because no fail-safe/graceful-degradation layer sits in
/// front of raw search-engine connectivity (unlike <c>CacheReadinessHealthCheck</c>'s
/// FusionCache-L1-absorption rationale). <c>PendingWriteCount</c>, <c>DocumentCount</c>,
/// <c>EngineVersion</c>, and <c>Latency</c> are surfaced via <see cref="HealthCheckResult.Data"/> as
/// informational values only — <c>PendingWriteCount</c> is <b>never</b> factored into the
/// Healthy/Unhealthy decision, per <c>09.Search/CLAUDE.md</c>'s own explicit rule that a deep write
/// backlog means results are stale, not unavailable, and failing readiness would remove serving
/// capacity exactly when it is most needed. <c>09.Search</c> ships only the probe primitive — this
/// adapter is the <c>13.ServiceDefaults</c>-owned <see cref="IHealthCheck"/> wiring per the platform's
/// "OTel/health check/probe wiring lives in 13.ServiceDefaults" rule.
/// </remarks>
internal sealed class SearchReadinessHealthCheck(ISearchIndexProvisioner provisioner, string indexName)
    : IHealthCheck
{
    /// <inheritdoc/>
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var result = await provisioner.ProbeAsync(indexName, cancellationToken).ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            return HealthCheckResult.Unhealthy(result.Error.Message);
        }

        var health = result.Value;
        var data = new Dictionary<string, object>
        {
            ["pendingWriteCount"] = health.PendingWriteCount.HasValue
                ? health.PendingWriteCount.Value
                : "n/a",
            ["documentCount"] = health.DocumentCount,
            ["engineVersion"] = health.EngineVersion,
            ["latency"] = health.Latency,
        };

        return health is { Reachable: true, IndexAddressable: true, Searchable: true }
            ? HealthCheckResult.Healthy("Search index reachable, addressable, and searchable.", data)
            : HealthCheckResult.Unhealthy(
                $"Search index '{indexName}' not ready (Reachable={health.Reachable}, " +
                $"IndexAddressable={health.IndexAddressable}, Searchable={health.Searchable}).",
                data: data);
    }
}

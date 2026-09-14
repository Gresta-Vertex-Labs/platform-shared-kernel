using Microsoft.Extensions.Diagnostics.HealthChecks;
using SharedKernel.Workflows.Temporal.Health;

namespace SharedKernel.ServiceDefaults.HealthChecks;

/// <summary>
/// Wraps <see cref="IWorkflowServiceProbe.ProbeAsync"/> (<c>17.Workflows</c>) in an
/// <see cref="IHealthCheck"/>.
/// </summary>
/// <remarks>
/// <para>
/// Reports <see cref="HealthStatus.Healthy"/> when the probe's <c>Result.IsSuccess</c> is
/// <see langword="true"/> and its <c>WorkflowServiceHealth.Reachable</c>,
/// <c>.NamespaceAddressable</c>, and <c>.WorkerPollersActive</c> members are all
/// <see langword="true"/>; <see cref="HealthStatus.Unhealthy"/> otherwise — calibrated like
/// <c>VectorStoreReadinessHealthCheck</c> and <c>SearchReadinessHealthCheck</c>, never
/// <see cref="HealthStatus.Degraded"/>, because no fail-safe/graceful-degradation layer sits in
/// front of raw workflow-service connectivity (unlike <c>CacheReadinessHealthCheck</c>'s
/// FusionCache-L1-absorption rationale). <c>WorkerPollersActive</c> is normalized to
/// <see langword="true"/> by the probe implementation itself on a client-only registration ("there
/// are no pollers to fail"), so a single unconditional conjunct correctly serves both
/// worker-hosting and client-only registrations with no caller-supplied disambiguation.
/// </para>
/// <para>
/// <c>TaskQueueBacklog</c> and <c>Latency</c> are surfaced via <see cref="HealthCheckResult.Data"/>
/// as informational values only — <c>TaskQueueBacklog</c> is <b>never</b> factored into the
/// Healthy/Unhealthy decision, per <c>17.Workflows/CLAUDE.md</c>'s own explicit rule that a deep
/// backlog means work is slow, not that the service is unavailable. <c>17.Workflows</c> ships only
/// the probe primitive — this adapter is the <c>13.ServiceDefaults</c>-owned
/// <see cref="IHealthCheck"/> wiring per the platform's "OTel/health check/probe wiring lives in
/// 13.ServiceDefaults" rule.
/// </para>
/// <para>
/// Reaches ONLY <see cref="IWorkflowServiceProbe"/>/<c>WorkflowServiceHealth</c> from
/// <c>SharedKernel.Workflows.Temporal</c> — the WO-047 layering exception is scoped exclusively to
/// those two types. No other <c>17.Workflows</c> type is referenced here.
/// </para>
/// </remarks>
internal sealed class WorkflowReadinessHealthCheck(IWorkflowServiceProbe probe) : IHealthCheck
{
    /// <inheritdoc/>
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var result = await probe.ProbeAsync(cancellationToken).ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            return HealthCheckResult.Unhealthy(result.Error.Message);
        }

        var health = result.Value;
        var data = new Dictionary<string, object>
        {
            ["workerPollersActive"] = health.WorkerPollersActive,
            ["taskQueueBacklog"] = health.TaskQueueBacklog.HasValue
                ? health.TaskQueueBacklog.Value
                : "n/a",
            ["latency"] = health.Latency,
        };

        return health is { Reachable: true, NamespaceAddressable: true, WorkerPollersActive: true }
            ? HealthCheckResult.Healthy("Workflow service reachable, namespace addressable, and worker pollers active.", data)
            : HealthCheckResult.Unhealthy(
                $"Workflow service not ready (Reachable={health.Reachable}, " +
                $"NamespaceAddressable={health.NamespaceAddressable}, " +
                $"WorkerPollersActive={health.WorkerPollersActive}).",
                data: data);
    }
}

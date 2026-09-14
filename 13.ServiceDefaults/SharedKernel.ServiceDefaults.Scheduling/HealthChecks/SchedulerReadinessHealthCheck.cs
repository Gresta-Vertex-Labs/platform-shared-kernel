using Microsoft.Extensions.Diagnostics.HealthChecks;
using SharedKernel.Scheduling.Probes;

namespace SharedKernel.ServiceDefaults.HealthChecks;

/// <summary>
/// Wraps <see cref="ISchedulerServiceProbe.ProbeAsync"/> (<c>19.Scheduling</c>) in an
/// <see cref="IHealthCheck"/>.
/// </summary>
/// <remarks>
/// <para>
/// Reports <see cref="HealthStatus.Healthy"/> when <see cref="SchedulerServiceHealth.IsRunning"/> is
/// <see langword="true"/>; <see cref="HealthStatus.Unhealthy"/> otherwise — never
/// <see cref="HealthStatus.Degraded"/>, because no fail-safe/graceful-degradation layer sits in
/// front of the hosted scheduling loop's own in-process running state (same calibration family as
/// <c>WorkflowReadinessHealthCheck</c>/<c>VectorStoreReadinessHealthCheck</c>, never
/// <c>CacheReadinessHealthCheck</c>'s FusionCache-L1-absorption rationale).
/// </para>
/// <para>
/// <b>Unlike <c>WorkflowReadinessHealthCheck</c>, <see cref="ISchedulerServiceProbe.ProbeAsync"/>
/// returns <see cref="SchedulerServiceHealth"/> directly — never a <c>Result&lt;T&gt;</c> wrapper</b>
/// (confirmed directly against the shipped <c>19.Scheduling</c> source) — there is no
/// <c>Result.IsSuccess</c> branch to check here. The probe's own implementation reads only
/// in-process state and performs no I/O, so it cannot practically throw.
/// </para>
/// <para>
/// <see cref="SchedulerServiceHealth.RegisteredJobCount"/> and
/// <see cref="SchedulerServiceHealth.LastTickUtc"/> are surfaced via
/// <see cref="HealthCheckResult.Data"/> as informational values only — <c>RegisteredJobCount</c> is
/// <b>never</b> factored into the Healthy/Unhealthy decision, directly mirroring the
/// <c>PendingWriteCount</c>/<c>TaskQueueBacklog</c> precedent already established for
/// <c>SearchReadinessHealthCheck</c>/<c>VectorStoreReadinessHealthCheck</c>/
/// <c>WorkflowReadinessHealthCheck</c> — a busy scheduler is not an unhealthy one.
/// <c>19.Scheduling</c> ships only the probe primitive — this adapter is the
/// <c>13.ServiceDefaults</c>-owned <see cref="IHealthCheck"/> wiring per the platform's
/// "OTel/health check/probe wiring lives in 13.ServiceDefaults" rule.
/// </para>
/// <para>
/// Reaches ONLY <see cref="ISchedulerServiceProbe"/>/<see cref="SchedulerServiceHealth"/> from
/// <c>SharedKernel.Scheduling</c> — the WO-073/P-466 layering exception is scoped exclusively to
/// those two types. No other <c>19.Scheduling</c> type is referenced here.
/// </para>
/// </remarks>
internal sealed class SchedulerReadinessHealthCheck(ISchedulerServiceProbe probe) : IHealthCheck
{
    /// <inheritdoc/>
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var health = await probe.ProbeAsync(cancellationToken).ConfigureAwait(false);

        var data = new Dictionary<string, object>
        {
            ["registeredJobCount"] = health.RegisteredJobCount,
            ["lastTickUtc"] = health.LastTickUtc.HasValue
                ? health.LastTickUtc.Value
                : "n/a",
        };

        return health.IsRunning
            ? HealthCheckResult.Healthy("Scheduler loop is running.", data)
            : HealthCheckResult.Unhealthy("Scheduler loop is not running.", data: data);
    }
}

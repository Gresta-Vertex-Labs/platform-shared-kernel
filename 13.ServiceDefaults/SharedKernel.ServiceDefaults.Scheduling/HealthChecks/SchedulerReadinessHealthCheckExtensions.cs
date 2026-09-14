using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using SharedKernel.Scheduling.Probes;

namespace SharedKernel.ServiceDefaults.HealthChecks;

/// <summary>
/// Opt-in scheduler-loop liveness health check, wrapping <c>19.Scheduling</c>'s
/// <see cref="ISchedulerServiceProbe.ProbeAsync"/> probe.
/// </summary>
public static class SchedulerReadinessHealthCheckExtensions
{
    /// <summary>
    /// Registers a health check that verifies the hosted scheduling loop is running, via the
    /// <see cref="ISchedulerServiceProbe"/> resolved from DI.
    /// </summary>
    /// <param name="builder">The health checks builder.</param>
    /// <param name="name">The health check registration name. Defaults to <see cref="HealthCheckNames.Scheduler"/>.</param>
    /// <returns>The same <paramref name="builder"/> instance, for fluent chaining.</returns>
    /// <remarks>
    /// <para>
    /// Tagged <see cref="HealthCheckTags.Ready"/> and <see cref="HealthCheckTags.Scheduler"/>, never
    /// <see cref="HealthCheckTags.Live"/>. Resolves only <see cref="ISchedulerServiceProbe"/> from
    /// DI — never <c>IScheduledJobRegistry</c>, <c>ScheduledCommandJob&lt;TCommand&gt;</c>,
    /// <c>SchedulingOptions</c>, <c>MisfirePolicy</c>/<c>OverlapPolicy</c>, or any other
    /// <c>19.Scheduling</c> type. Like <c>WorkflowReadinessHealthCheckExtensions.AddWorkflowReadinessCheck</c>
    /// (and unlike the bucket/indexName/collectionName family), this method takes no
    /// caller-supplied identifier parameter: <see cref="ISchedulerServiceProbe"/> is a per-host
    /// singleton with nothing analogous to a bucket/index/collection name to disambiguate.
    /// </para>
    /// <para>
    /// <b>Layering note:</b> resolving <see cref="ISchedulerServiceProbe"/> requires a
    /// <c>ProjectReference</c> from <c>SharedKernel.ServiceDefaults</c> (layer 13) to
    /// <c>SharedKernel.Scheduling</c> (layer 19) — a narrow, individually-named exception recorded
    /// in the root <c>CLAUDE.md</c>'s Layering Rules → Hard rules section
    /// (<c>13.ServiceDefaults</c> may take a <c>ProjectReference</c> to <c>SharedKernel.Scheduling</c>
    /// solely to resolve <see cref="ISchedulerServiceProbe"/>/<see cref="SchedulerServiceHealth"/>).
    /// This is a new, <b>independently-earned</b> grant — it does not widen, and must never be
    /// reasoned about by analogy to, the existing <c>17.Workflows</c>/WO-047 grant that
    /// <c>WorkflowReadinessHealthCheckExtensions.AddWorkflowReadinessCheck</c> relies on; the
    /// two grants are independently scoped and independently justified.
    /// </para>
    /// <para>
    /// Reports <see cref="HealthStatus.Unhealthy"/> — never <see cref="HealthStatus.Degraded"/> —
    /// when the scheduling loop is not running. A large <see cref="SchedulerServiceHealth.RegisteredJobCount"/>
    /// is surfaced only as informational data and is never treated as a readiness failure. Opt-in
    /// only — never registered by <c>AddServiceDefaults()</c> or <c>AddSharedKernelHealthChecks()</c>.
    /// </para>
    /// </remarks>
    public static IHealthChecksBuilder AddSchedulerReadinessCheck(
        this IHealthChecksBuilder builder,
        string name = HealthCheckNames.Scheduler)
    {
        ArgumentNullException.ThrowIfNull(builder);

        string[] tags = [HealthCheckTags.Ready, HealthCheckTags.Scheduler];

        HealthCheckRegistrationLogging.LogRegistration(
            builder.Services,
            "SharedKernel.ServiceDefaults.HealthChecks.SchedulerReadinessHealthCheckExtensions",
            name,
            tags);

        return builder.Add(new HealthCheckRegistration(
            name,
            sp => new SchedulerReadinessHealthCheck(sp.GetRequiredService<ISchedulerServiceProbe>()),
            failureStatus: null,
            tags: tags));
    }
}

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using SharedKernel.Workflows.Temporal.Health;

namespace SharedKernel.ServiceDefaults.HealthChecks;

/// <summary>
/// Opt-in workflow-service connectivity health check, wrapping <c>17.Workflows</c>'s
/// <see cref="IWorkflowServiceProbe.ProbeAsync"/> probe.
/// </summary>
public static class WorkflowReadinessHealthCheckExtensions
{
    /// <summary>
    /// Registers a health check that verifies Temporal service connectivity — and, on a
    /// worker-hosting composition, this process's own worker pollers — via the
    /// <see cref="IWorkflowServiceProbe"/> resolved from DI.
    /// </summary>
    /// <param name="builder">The health checks builder.</param>
    /// <param name="name">The health check registration name. Defaults to <see cref="HealthCheckNames.Workflows"/>.</param>
    /// <returns>The same <paramref name="builder"/> instance, for fluent chaining.</returns>
    /// <remarks>
    /// <para>
    /// Tagged <see cref="HealthCheckTags.Ready"/> and <see cref="HealthCheckTags.Workflows"/>, never
    /// <see cref="HealthCheckTags.Live"/>. Resolves only <see cref="IWorkflowServiceProbe"/> from
    /// DI — never <c>TemporalOptions</c>, never <c>ITemporalClient</c>, never
    /// <c>WorkflowBase</c>/<c>ActivityBase</c>, <c>IWorkflowDispatcher</c>,
    /// <c>ITemporalRawClientAccessor</c>, or any other <c>17.Workflows</c> type. Unlike the
    /// bucket/indexName/collectionName family (Storage/Search/VectorStore), this method takes no
    /// caller-supplied identifier parameter: <see cref="IWorkflowServiceProbe"/> is a per-host
    /// singleton with nothing analogous to a bucket/index/collection name to disambiguate.
    /// </para>
    /// <para>
    /// Reports <see cref="HealthStatus.Unhealthy"/> — never <see cref="HealthStatus.Degraded"/> —
    /// when the underlying probe fails or reports the service as unreachable, its namespace
    /// unaddressable, or its worker pollers inactive. A deep <c>TaskQueueBacklog</c> is surfaced
    /// only as informational data and is never treated as a readiness failure. Opt-in only — never
    /// registered by <c>AddServiceDefaults()</c> or <c>AddSharedKernelHealthChecks()</c>.
    /// </para>
    /// </remarks>
    public static IHealthChecksBuilder AddWorkflowReadinessCheck(
        this IHealthChecksBuilder builder,
        string name = HealthCheckNames.Workflows)
    {
        ArgumentNullException.ThrowIfNull(builder);

        string[] tags = [HealthCheckTags.Ready, HealthCheckTags.Workflows];

        HealthCheckRegistrationLogging.LogRegistration(
            builder.Services,
            "SharedKernel.ServiceDefaults.HealthChecks.WorkflowReadinessHealthCheckExtensions",
            name,
            tags);

        return builder.Add(new HealthCheckRegistration(
            name,
            sp => new WorkflowReadinessHealthCheck(sp.GetRequiredService<IWorkflowServiceProbe>()),
            failureStatus: null,
            tags: tags));
    }
}

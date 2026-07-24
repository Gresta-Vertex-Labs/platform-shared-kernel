using SharedKernel.Primitives.Results;

namespace SharedKernel.Workflows.Temporal.Health;

/// <summary>
/// A zero-dependency readiness probe primitive for the Temporal service and, on a worker-hosting
/// composition, this process's own worker pollers.
/// </summary>
/// <remarks>
/// This is a primitive, not a health check — <c>17.Workflows</c> ships no <c>IHealthCheck</c>
/// implementation and never references <c>Microsoft.Extensions.Diagnostics.HealthChecks</c>. Wiring
/// this into <c>AddHealthChecks()</c> is <c>13.ServiceDefaults</c>'s responsibility, which resolves
/// only this interface — never <c>TemporalOptions</c>, never a raw Temporal client type.
/// </remarks>
public interface IWorkflowServiceProbe
{
    /// <summary>Probes the Temporal service and, where applicable, this process's worker pollers.</summary>
    /// <param name="cancellationToken">A token to cancel the probe.</param>
    Task<Result<WorkflowServiceHealth>> ProbeAsync(CancellationToken cancellationToken = default);
}

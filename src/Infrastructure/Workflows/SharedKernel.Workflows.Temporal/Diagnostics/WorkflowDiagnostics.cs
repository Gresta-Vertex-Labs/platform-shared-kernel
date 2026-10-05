using System.Diagnostics;
using System.Diagnostics.Metrics;
using SharedKernel.Workflows.Temporal.Constants;

namespace SharedKernel.Workflows.Temporal.Diagnostics;

/// <summary>
/// Holds this domain's <see cref="ActivitySource"/> and <see cref="Meter"/>, named from
/// <see cref="WorkflowWellKnown.ActivitySourceName"/>/<see cref="WorkflowWellKnown.MeterName"/>.
/// </summary>
/// <remarks>
/// <c>13.ServiceDefaults</c> wires these string-name-only via <c>WithWorkflowTelemetry()</c> with no
/// <c>ProjectReference</c> to this package — the byte-identical name is what makes that work.
/// Nothing in this package starts an <see cref="Activity"/> inside workflow code; workflow tracing is
/// <c>TracingInterceptor</c>'s job.
/// </remarks>
internal static class WorkflowDiagnostics
{
    /// <summary>Gets this domain's <see cref="ActivitySource"/>.</summary>
    public static ActivitySource ActivitySource { get; } = new(WorkflowWellKnown.ActivitySourceName);

    /// <summary>Gets this domain's <see cref="Meter"/>.</summary>
    public static Meter Meter { get; } = new(WorkflowWellKnown.MeterName);
}

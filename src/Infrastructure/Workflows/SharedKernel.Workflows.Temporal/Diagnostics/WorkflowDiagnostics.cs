using System.Diagnostics.Metrics;
using SharedKernel.Primitives.Telemetry;
using SharedKernel.Workflows.Temporal.Constants;

namespace SharedKernel.Workflows.Temporal.Diagnostics;

/// <summary>
/// Holds this domain's <see cref="Meter"/>, named from <see cref="WorkflowWellKnown.MeterName"/>.
/// </summary>
/// <remarks>
/// <para>
/// This package owns no <see cref="System.Diagnostics.ActivitySource"/>: client, workflow and activity spans are
/// the Temporal <c>TracingInterceptor</c>'s, and workflow code may not touch the <c>Activity</c> API. The meter
/// is the sink <c>WithMetrics()</c> hands to the SDK's <c>CustomMetricMeter</c>; the instruments on it are named by
/// the SDK.
/// </para>
/// <para>
/// <c>13.ServiceDefaults</c> subscribes to this meter through its <c>SharedKernel.*</c> wildcard, with no
/// <c>ProjectReference</c> to this package.
/// </para>
/// </remarks>
internal static class WorkflowDiagnostics
{
    /// <summary>Gets this domain's <see cref="Meter"/>, versioned from the assembly informational version.</summary>
    public static Meter Meter { get; } = new(
        WorkflowWellKnown.MeterName,
        TelemetryVersion.Of(typeof(WorkflowDiagnostics).Assembly));
}

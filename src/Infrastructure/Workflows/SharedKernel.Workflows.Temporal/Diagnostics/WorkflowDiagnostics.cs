using System.Diagnostics.Metrics;
using SharedKernel.Primitives.Telemetry;
using SharedKernel.Workflows.Temporal.Constants;

namespace SharedKernel.Workflows.Temporal.Diagnostics;

/// <summary>
/// The name and version of this domain's <see cref="Meter"/>, which the host's <see cref="IMeterFactory"/> creates.
/// </summary>
/// <remarks>
/// <para>
/// This package owns no <see cref="System.Diagnostics.ActivitySource"/>: client, workflow and activity spans are
/// the Temporal <c>TracingInterceptor</c>'s, and workflow code may not touch the <c>Activity</c> API. The meter
/// is the sink <c>WithMetrics()</c> hands to the SDK's <c>CustomMetricMeter</c>; the instruments on it are named by
/// the SDK, so they are exempt from the <c>sharedkernel.{capability}.*</c> naming convention.
/// </para>
/// <para>
/// <c>13.ServiceDefaults</c> subscribes to this meter through its <c>SharedKernel.*</c> wildcard, with no
/// <c>ProjectReference</c> to this package.
/// </para>
/// </remarks>
internal static class WorkflowDiagnostics
{
    private static readonly string Version = TelemetryVersion.Of(typeof(WorkflowDiagnostics).Assembly);

    /// <summary>Creates this domain's <see cref="Meter"/> from <paramref name="meterFactory"/>, versioned from the assembly informational version.</summary>
    /// <param name="meterFactory">The host's meter factory.</param>
    /// <returns>The meter named <see cref="WorkflowWellKnown.MeterName"/>.</returns>
    public static Meter CreateMeter(IMeterFactory meterFactory)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);
        return meterFactory.Create(new MeterOptions(WorkflowWellKnown.MeterName) { Version = Version });
    }
}

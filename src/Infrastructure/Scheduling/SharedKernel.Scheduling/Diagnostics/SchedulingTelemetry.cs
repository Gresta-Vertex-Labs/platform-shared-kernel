using System.Diagnostics;
using SharedKernel.Primitives.Telemetry;

namespace SharedKernel.Scheduling.Diagnostics;

/// <summary>
/// The telemetry name of this package: the <see cref="ActivitySource"/> and the
/// <see cref="System.Diagnostics.Metrics.Meter"/> both carry it, and <c>AddServiceDefaults</c> subscribes to it
/// through its <c>SharedKernel.*</c> wildcard.
/// </summary>
/// <remarks>
/// The name is public so the host and the governance checks can refer to it. The source and the instruments
/// stay internal: the source is a process-lifetime static (the standard OpenTelemetry-in-.NET shape), the
/// instruments are built from <see cref="System.Diagnostics.Metrics.IMeterFactory"/> by <c>SchedulingMetrics</c>.
/// </remarks>
public static class SchedulingTelemetry
{
    /// <summary>The name shared by the activity source and the meter.</summary>
    public const string Name = "SharedKernel.Scheduling";

    internal static readonly ActivitySource ActivitySource =
        new(Name, TelemetryVersion.Of(typeof(SchedulingTelemetry).Assembly));
}

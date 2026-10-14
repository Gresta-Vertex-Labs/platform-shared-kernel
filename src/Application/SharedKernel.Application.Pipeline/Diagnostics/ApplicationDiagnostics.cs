using System.Diagnostics;
using SharedKernel.Primitives.Telemetry;

namespace SharedKernel.Application.Pipeline;

/// <summary>
/// The telemetry identity of the Application packages: the <see cref="System.Diagnostics.ActivitySource"/> and
/// <see cref="System.Diagnostics.Metrics.Meter"/> names every span and instrument of
/// <c>SharedKernel.Application.Pipeline</c> and <c>SharedKernel.Application.Pipeline.Caching</c> is recorded under.
/// </summary>
/// <remarks>
/// <para>
/// A host that subscribes by name (or a test that listens for one source) uses these constants instead of typing the
/// literal. <c>AddServiceDefaults</c> already subscribes to every <c>SharedKernel.*</c> source and meter by wildcard, so
/// a service needs nothing else.
/// </para>
/// <para>
/// The source and every meter carry the assembly's informational version (<see cref="TelemetryVersion.Of"/>). The
/// <see cref="System.Diagnostics.ActivitySource"/> is a process-lifetime static, the one documented exception to the
/// no-static-state rule; meters come from <c>IMeterFactory</c>.
/// </para>
/// </remarks>
public static class ApplicationDiagnostics
{
    /// <summary>The name of the <see cref="System.Diagnostics.ActivitySource"/> (<c>"SharedKernel.Application"</c>).</summary>
    public const string ActivitySourceName = "SharedKernel.Application";

    /// <summary>The name of the <see cref="System.Diagnostics.Metrics.Meter"/> (<c>"SharedKernel.Application"</c>).</summary>
    public const string MeterName = "SharedKernel.Application";

    /// <summary>The version the source and the meters report.</summary>
    internal static readonly string TelemetryVersionText = TelemetryVersion.Of(typeof(ApplicationDiagnostics).Assembly);

    /// <summary>The shared <see cref="System.Diagnostics.ActivitySource"/> for this domain.</summary>
    internal static readonly ActivitySource ActivitySource = new(ActivitySourceName, TelemetryVersionText);
}

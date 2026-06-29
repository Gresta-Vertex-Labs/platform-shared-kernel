using System.Diagnostics.Metrics;

namespace SharedKernel.Application.Behaviors.Metrics;

/// <summary>
/// Static diagnostics instruments for the application pipeline.
/// </summary>
/// <remarks>
/// This is the only sanctioned static state in this domain — the same platform-standard
/// diagnostics-instrument pattern already approved for <c>07.Messaging</c>'s
/// <c>MessagingDiagnostics.ActivitySource</c>. A static <see cref="Meter"/>/<see cref="Histogram{T}"/>
/// pair carries no mutable business state; the BCL diagnostics API is explicitly designed around
/// process-lifetime static instrument instances. Do not add further ad-hoc static fields under
/// cover of this exception. <c>13.ServiceDefaults</c> (future work) registers the
/// <c>"SharedKernel.Application"</c> meter name with the host's <c>MeterProvider</c> — this domain
/// never reaches into <c>13.ServiceDefaults</c>.
/// </remarks>
internal static class ApplicationDiagnostics
{
    /// <summary>The meter used by all instruments in this domain.</summary>
    internal static readonly Meter Meter = new("SharedKernel.Application", "1.0.0");

    /// <summary>Records the duration, in milliseconds, of a single request's pipeline traversal.</summary>
    internal static readonly Histogram<double> RequestDuration =
        Meter.CreateHistogram<double>("sharedkernel.application.request.duration", unit: "ms");
}

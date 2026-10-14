using System.Diagnostics.Metrics;
using OpenTelemetry.Metrics;

namespace SharedKernel.ServiceDefaults.Telemetry;

/// <summary>
/// The second-scale bucket view applied to every <c>SharedKernel.*</c> duration histogram.
/// </summary>
/// <remarks>
/// <para>
/// The OpenTelemetry SDK's default boundaries (<c>0, 5, 10, 25 ... 10000</c>) assume milliseconds; applied to a
/// histogram recorded in seconds they would put nearly every measurement into the first bucket. A duration histogram
/// of the kernel is a <see cref="Histogram{T}"/> of <see cref="double"/> with unit <c>s</c>; those, and only those, in
/// a meter named <c>SharedKernel.*</c> get <see cref="BucketBoundariesSeconds"/>. A histogram in any other unit, of
/// another value type or in another vendor's meter keeps the SDK defaults.
/// </para>
/// <para>
/// The predicate is a type pattern, never reflection.
/// </para>
/// </remarks>
internal static class DurationHistogramView
{
    /// <summary>The unit of a kernel duration histogram.</summary>
    internal const string SecondsUnit = "s";

    /// <summary>
    /// Explicit bucket boundaries, in seconds: the OpenTelemetry semantic-convention boundaries for request-duration
    /// histograms (<c>http.server.request.duration</c>).
    /// </summary>
    internal static readonly double[] BucketBoundariesSeconds =
        [0.005, 0.01, 0.025, 0.05, 0.075, 0.1, 0.25, 0.5, 0.75, 1, 2.5, 5, 7.5, 10];

    /// <summary>Returns the second-scale configuration for a kernel duration histogram, else <see langword="null"/>.</summary>
    /// <param name="instrument">The instrument the SDK is about to create a metric stream for.</param>
    internal static MetricStreamConfiguration? Select(Instrument instrument) =>
        instrument is Histogram<double>
        && string.Equals(instrument.Unit, SecondsUnit, StringComparison.Ordinal)
        && instrument.Meter.Name.StartsWith(TelemetryNames.SharedKernelPrefix, StringComparison.Ordinal)
            ? new ExplicitBucketHistogramConfiguration { Boundaries = BucketBoundariesSeconds }
            : null;
}

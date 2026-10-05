using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace SharedKernel.ServiceDefaults.Telemetry;

/// <summary>
/// Wires the pre-existing <c>"SharedKernel.Reporting"</c> <see cref="System.Diagnostics.ActivitySource"/>
/// and <see cref="System.Diagnostics.Metrics.Meter"/> — both owned and emitted by <c>20.Reporting</c>'s
/// <c>SharedKernel.Reporting.Abstractions</c>, shared by every exporter and HTML-to-PDF converter — into the
/// host's <c>TracerProvider</c>/<c>MeterProvider</c>.
/// </summary>
public static class ReportingTelemetryExtensions
{
    private const string ReportingInstrumentationName = "SharedKernel.Reporting";

    /// <summary>
    /// Adds the <c>"SharedKernel.Reporting"</c> <see cref="System.Diagnostics.ActivitySource"/> name (spans named
    /// <c>reporting export</c> and <c>reporting convert</c>, tagged with the format, the operation, the store, the
    /// row count and the size) to the host's <c>TracerProvider</c>, and the <c>"SharedKernel.Reporting"</c> meter
    /// (instruments: <c>reporting.operation.duration</c>, <c>reporting.rows</c> and <c>reporting.bytes</c>) to the
    /// host's <c>MeterProvider</c>.
    /// </summary>
    /// <param name="builder">The host application builder.</param>
    /// <returns>The same <paramref name="builder"/> instance, for fluent chaining.</returns>
    /// <remarks>
    /// Registers by string name only — the <c>SharedKernel.ServiceDefaults</c> composition base takes no
    /// <c>ProjectReference</c> to any <c>20.Reporting</c> package. Idempotent: the OpenTelemetry SDK no-ops when the
    /// same source/meter name is added more than once.
    /// </remarks>
    public static IHostApplicationBuilder WithReportingTelemetry(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services
            .AddOpenTelemetry()
            .WithTracing(tracing => tracing.AddSource(ReportingInstrumentationName))
            .WithMetrics(metrics => metrics.AddMeter(ReportingInstrumentationName));

        return builder;
    }
}

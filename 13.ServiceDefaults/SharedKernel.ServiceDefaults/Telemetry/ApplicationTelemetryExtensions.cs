using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace SharedKernel.ServiceDefaults.Telemetry;

/// <summary>
/// Wires the pre-existing <c>"SharedKernel.Application"</c> <see cref="System.Diagnostics.ActivitySource"/>
/// and <see cref="System.Diagnostics.Metrics.Meter"/> — owned and emitted by
/// <c>05.Application.Behaviors</c>'s internal <c>ApplicationDiagnostics</c> static class — into the
/// host's <c>TracerProvider</c>/<c>MeterProvider</c>.
/// </summary>
public static class ApplicationTelemetryExtensions
{
    private const string SharedKernelApplicationInstrumentationName = "SharedKernel.Application";

    /// <summary>
    /// Adds the <c>"SharedKernel.Application"</c> <see cref="System.Diagnostics.ActivitySource"/> name
    /// to the host's <c>TracerProvider</c>, and the <c>"SharedKernel.Application"</c> meter to the
    /// host's <c>MeterProvider</c>.
    /// </summary>
    /// <param name="builder">The host application builder.</param>
    /// <returns>The same <paramref name="builder"/> instance, for fluent chaining.</returns>
    /// <remarks>
    /// <para>
    /// <c>13.ServiceDefaults</c> does not create this source/meter pair — both are created and used
    /// entirely within <c>SharedKernel.Application.Behaviors</c>: <c>TracingBehavior&lt;,&gt;</c>
    /// starts spans from the <see cref="System.Diagnostics.ActivitySource"/>, and
    /// <c>MetricsBehavior&lt;,&gt;</c> records the <c>sharedkernel.application.request.duration</c>
    /// histogram from the <see cref="System.Diagnostics.Metrics.Meter"/>. This method only registers
    /// the already-existing source/meter name with the host's <c>TracerProvider</c>/<c>MeterProvider</c>
    /// via <see cref="TracerProviderBuilder.AddSource(string[])"/> and
    /// <see cref="MeterProviderBuilder.AddMeter(string[])"/> — by string name only, since
    /// <c>ApplicationDiagnostics</c> is <c>internal</c> to its own assembly with no
    /// <c>InternalsVisibleTo</c> grant to <c>SharedKernel.ServiceDefaults</c>. No new
    /// <c>ProjectReference</c> to any <c>SharedKernel.Application.*</c> package is added or needed.
    /// </para>
    /// <para>
    /// Idempotent: calling this method more than once on the same <paramref name="builder"/>
    /// registers no duplicate instrument — the underlying OpenTelemetry SDK no-ops when the same
    /// source/meter name is added to a <c>TracerProviderBuilder</c>/<c>MeterProviderBuilder</c> more
    /// than once.
    /// </para>
    /// </remarks>
    public static IHostApplicationBuilder WithApplicationTelemetry(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services
            .AddOpenTelemetry()
            .WithTracing(tracing => tracing.AddSource(SharedKernelApplicationInstrumentationName))
            .WithMetrics(metrics => metrics.AddMeter(SharedKernelApplicationInstrumentationName));

        return builder;
    }
}

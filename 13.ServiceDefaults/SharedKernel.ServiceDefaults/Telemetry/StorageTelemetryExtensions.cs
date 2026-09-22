using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace SharedKernel.ServiceDefaults.Telemetry;

/// <summary>
/// Wires the pre-existing <c>"SharedKernel.Storage"</c> <see cref="System.Diagnostics.ActivitySource"/>
/// and <see cref="System.Diagnostics.Metrics.Meter"/> — both owned and emitted by <c>08.Storage</c>'s
/// S3-family providers (<c>SharedKernel.Storage.S3</c>, and <c>SharedKernel.Storage.Obs</c> through it) —
/// into the host's <c>TracerProvider</c>/<c>MeterProvider</c>.
/// </summary>
public static class StorageTelemetryExtensions
{
    private const string StorageInstrumentationName = "SharedKernel.Storage";

    /// <summary>
    /// Adds the <c>"SharedKernel.Storage"</c> <see cref="System.Diagnostics.ActivitySource"/> name
    /// (client spans named <c>storage {operation}</c>, e.g. <c>storage upload</c>, tagged with the
    /// store, operation and provider) to the host's <c>TracerProvider</c>, and the
    /// <c>"SharedKernel.Storage"</c> meter (instruments: <c>storage.client.operation.duration</c> and
    /// <c>storage.client.bytes</c>) to the host's <c>MeterProvider</c>.
    /// </summary>
    /// <param name="builder">The host application builder.</param>
    /// <returns>The same <paramref name="builder"/> instance, for fluent chaining.</returns>
    /// <remarks>
    /// <para>
    /// <c>13.ServiceDefaults</c> does not create this source/meter pair — both are created and used
    /// entirely within <c>SharedKernel.Storage.S3</c>. This method only registers the already-existing
    /// source/meter name with the host's <c>TracerProvider</c>/<c>MeterProvider</c> via
    /// <see cref="TracerProviderBuilder.AddSource(string[])"/> and
    /// <see cref="MeterProviderBuilder.AddMeter(string[])"/> — by string name only, since the
    /// <c>SharedKernel.ServiceDefaults</c> composition base deliberately takes no
    /// <c>ProjectReference</c> to any <c>08.Storage</c> package.
    /// </para>
    /// <para>
    /// Idempotent: calling this method more than once on the same <paramref name="builder"/>
    /// registers no duplicate instrument — the underlying OpenTelemetry SDK no-ops when the same
    /// source/meter name is added to a <c>TracerProviderBuilder</c>/<c>MeterProviderBuilder</c> more
    /// than once.
    /// </para>
    /// </remarks>
    public static IHostApplicationBuilder WithStorageTelemetry(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services
            .AddOpenTelemetry()
            .WithTracing(tracing => tracing.AddSource(StorageInstrumentationName))
            .WithMetrics(metrics => metrics.AddMeter(StorageInstrumentationName));

        return builder;
    }
}

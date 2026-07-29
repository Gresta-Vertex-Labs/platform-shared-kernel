using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace SharedKernel.ServiceDefaults.Telemetry;

/// <summary>
/// Wires the pre-existing <c>"SharedKernel.Caching"</c> <see cref="System.Diagnostics.ActivitySource"/>
/// and <see cref="System.Diagnostics.Metrics.Meter"/> — both owned and emitted by <c>02.Caching</c>'s
/// <c>SharedKernel.Caching.FusionCache.Implementations.FusionCacheService</c> — into the host's
/// <c>TracerProvider</c>/<c>MeterProvider</c>.
/// </summary>
public static class CachingTelemetryExtensions
{
    private const string CachingInstrumentationName = "SharedKernel.Caching";

    /// <summary>
    /// Adds the <c>"SharedKernel.Caching"</c> <see cref="System.Diagnostics.ActivitySource"/> name
    /// (spans: <c>cache.get</c>, <c>cache.set</c>, <c>cache.get_or_set</c>) to the host's
    /// <c>TracerProvider</c>, and the <c>"SharedKernel.Caching"</c> meter (instruments:
    /// <c>cache.hits</c>, <c>cache.misses</c>, <c>cache.errors</c>, <c>cache.evictions</c>, and
    /// <c>cache.factory.duration</c>) to the host's <c>MeterProvider</c>.
    /// </summary>
    /// <param name="builder">The host application builder.</param>
    /// <returns>The same <paramref name="builder"/> instance, for fluent chaining.</returns>
    /// <remarks>
    /// <para>
    /// <c>13.ServiceDefaults</c> does not create this source/meter pair — both are created and used
    /// entirely within <c>SharedKernel.Caching.FusionCache</c>. This method only registers the
    /// already-existing source/meter name with the host's <c>TracerProvider</c>/<c>MeterProvider</c>
    /// via <see cref="TracerProviderBuilder.AddSource(string[])"/> and
    /// <see cref="MeterProviderBuilder.AddMeter(string[])"/> — by string name only, since this domain
    /// deliberately takes no <c>ProjectReference</c> to <c>SharedKernel.Caching.FusionCache</c>.
    /// </para>
    /// <para>
    /// Idempotent: calling this method more than once on the same <paramref name="builder"/>
    /// registers no duplicate instrument — the underlying OpenTelemetry SDK no-ops when the same
    /// source/meter name is added to a <c>TracerProviderBuilder</c>/<c>MeterProviderBuilder</c> more
    /// than once.
    /// </para>
    /// </remarks>
    public static IHostApplicationBuilder WithCachingTelemetry(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services
            .AddOpenTelemetry()
            .WithTracing(tracing => tracing.AddSource(CachingInstrumentationName))
            .WithMetrics(metrics => metrics.AddMeter(CachingInstrumentationName));

        return builder;
    }
}

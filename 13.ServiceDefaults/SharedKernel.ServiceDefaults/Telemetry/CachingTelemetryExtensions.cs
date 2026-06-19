using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Metrics;

namespace SharedKernel.ServiceDefaults.Telemetry;

/// <summary>
/// Wires the pre-existing <c>"SharedKernel.Caching"</c> meter (owned and emitted by
/// <c>02.Caching</c>'s <c>FusionCacheService</c>) into the host's <c>MeterProvider</c>.
/// </summary>
public static class CachingTelemetryExtensions
{
    private const string CachingMeterName = "SharedKernel.Caching";

    /// <summary>
    /// Adds the <c>"SharedKernel.Caching"</c> meter (instruments: <c>cache.hits</c>,
    /// <c>cache.misses</c>, <c>cache.errors</c>, <c>cache.evictions</c>, and
    /// <c>cache.factory.duration</c>) to the host's <c>MeterProvider</c>.
    /// </summary>
    /// <param name="builder">The host application builder.</param>
    /// <returns>The same <paramref name="builder"/> instance, for fluent chaining.</returns>
    /// <remarks>
    /// <para>
    /// <c>13.ServiceDefaults</c> does not create this meter — it is created and used entirely
    /// within <c>SharedKernel.Caching.FusionCache</c>. This method only registers the
    /// already-existing meter name with the host's <c>MeterProvider</c> via
    /// <see cref="MeterProviderBuilder.AddMeter(string[])"/>.
    /// </para>
    /// <para>
    /// Idempotent: calling this method more than once on the same <paramref name="builder"/>
    /// registers no duplicate instrument — the underlying OpenTelemetry SDK no-ops when the same
    /// meter name is added to a <c>MeterProviderBuilder</c> more than once.
    /// </para>
    /// </remarks>
    public static IHostApplicationBuilder WithCachingTelemetry(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services
            .AddOpenTelemetry()
            .WithMetrics(metrics => metrics.AddMeter(CachingMeterName));

        return builder;
    }
}

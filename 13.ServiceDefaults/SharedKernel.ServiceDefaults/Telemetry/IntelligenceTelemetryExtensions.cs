using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace SharedKernel.ServiceDefaults.Telemetry;

/// <summary>
/// Wires the pre-existing <c>"SharedKernel.AI"</c> <see cref="System.Diagnostics.ActivitySource"/>
/// and <see cref="System.Diagnostics.Metrics.Meter"/> — owned and emitted separately by each of
/// <c>10.Intelligence</c>'s <c>SharedKernel.AI.Qdrant</c>, <c>SharedKernel.AI.Milvus</c>, and
/// <c>SharedKernel.AI.SemanticKernel</c> provider packages under their own internal diagnostics
/// classes — into the host's <c>TracerProvider</c>/<c>MeterProvider</c>.
/// </summary>
public static class IntelligenceTelemetryExtensions
{
    private const string IntelligenceInstrumentationName = "SharedKernel.AI";

    /// <summary>
    /// Adds the <c>"SharedKernel.AI"</c> <see cref="System.Diagnostics.ActivitySource"/> name to
    /// the host's <c>TracerProvider</c>, and the <c>"SharedKernel.AI"</c> meter to the host's
    /// <c>MeterProvider</c>.
    /// </summary>
    /// <param name="builder">The host application builder.</param>
    /// <returns>The same <paramref name="builder"/> instance, for fluent chaining.</returns>
    /// <remarks>
    /// <para>
    /// <c>13.ServiceDefaults</c> does not create this source/meter pair — each of
    /// <c>SharedKernel.AI.Qdrant</c>, <c>SharedKernel.AI.Milvus</c>, and
    /// <c>SharedKernel.AI.SemanticKernel</c> creates its own separate
    /// <see cref="System.Diagnostics.ActivitySource"/>/<see cref="System.Diagnostics.Metrics.Meter"/>
    /// instance, all named from <c>10.Intelligence</c>'s
    /// <c>IntelligenceWellKnown.ActivitySourceName</c>/<c>.MeterName</c> constants (both
    /// <c>"SharedKernel.AI"</c>). This method only registers the already-existing source/meter name
    /// with the host's <c>TracerProvider</c>/<c>MeterProvider</c> via
    /// <see cref="TracerProviderBuilder.AddSource(string[])"/> and
    /// <see cref="MeterProviderBuilder.AddMeter(string[])"/> — by string name only, since this
    /// domain deliberately takes no <c>ProjectReference</c> to any <c>10.Intelligence</c> provider
    /// package for this wiring. One string name therefore covers telemetry for all three providers
    /// uniformly, with zero provider-specific branching.
    /// </para>
    /// <para>
    /// Idempotent: calling this method more than once on the same <paramref name="builder"/>
    /// registers no duplicate instrument — the underlying OpenTelemetry SDK no-ops when the same
    /// source/meter name is added to a <c>TracerProviderBuilder</c>/<c>MeterProviderBuilder</c> more
    /// than once.
    /// </para>
    /// </remarks>
    public static IHostApplicationBuilder WithIntelligenceTelemetry(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services
            .AddOpenTelemetry()
            .WithTracing(tracing => tracing.AddSource(IntelligenceInstrumentationName))
            .WithMetrics(metrics => metrics.AddMeter(IntelligenceInstrumentationName));

        return builder;
    }
}

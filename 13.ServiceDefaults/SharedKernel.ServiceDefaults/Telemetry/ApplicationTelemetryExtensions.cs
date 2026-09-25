using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace SharedKernel.ServiceDefaults.Telemetry;

/// <summary>
/// Wires the <c>"SharedKernel.Application"</c> <see cref="System.Diagnostics.ActivitySource"/> and
/// <see cref="System.Diagnostics.Metrics.Meter"/> — both owned and emitted by
/// <c>SharedKernel.Application.Pipeline</c> — into the host's <c>TracerProvider</c>/<c>MeterProvider</c>.
/// </summary>
public static class ApplicationTelemetryExtensions
{
    private const string SharedKernelApplicationInstrumentationName = "SharedKernel.Application";

    /// <summary>
    /// The request-duration histogram recorded by <c>MetricsBehavior&lt;,&gt;</c>, in seconds.
    /// </summary>
    internal const string RequestDurationInstrumentName = "sharedkernel.application.request.duration";

    /// <summary>
    /// Explicit bucket boundaries, in seconds, for <see cref="RequestDurationInstrumentName"/>.
    /// </summary>
    /// <remarks>
    /// The OpenTelemetry semantic-convention boundaries for request-duration histograms
    /// (<c>http.server.request.duration</c>). The SDK's default boundaries
    /// (<c>0, 5, 10, 25 … 10000</c>) assume milliseconds; applied to a seconds histogram they would
    /// put nearly every request into the first bucket.
    /// </remarks>
    internal static readonly double[] RequestDurationBucketBoundariesSeconds =
        [0.005, 0.01, 0.025, 0.05, 0.075, 0.1, 0.25, 0.5, 0.75, 1, 2.5, 5, 7.5, 10];

    /// <summary>
    /// Adds the <c>"SharedKernel.Application"</c> <see cref="System.Diagnostics.ActivitySource"/> name
    /// to the host's <c>TracerProvider</c>, and the <c>"SharedKernel.Application"</c> meter — with
    /// seconds-scaled bucket boundaries for its request-duration histogram — to the host's
    /// <c>MeterProvider</c>.
    /// </summary>
    /// <param name="builder">The host application builder.</param>
    /// <returns>The same <paramref name="builder"/> instance, for fluent chaining.</returns>
    /// <remarks>
    /// <para>
    /// <c>13.ServiceDefaults</c> does not create this source/meter pair. In
    /// <c>SharedKernel.Application.Pipeline</c>, <c>TracingBehavior&lt;,&gt;</c> starts one span per
    /// request from the <see cref="System.Diagnostics.ActivitySource"/>, named after the request
    /// type's short name, and <c>MetricsBehavior&lt;,&gt;</c> records the
    /// <c>sharedkernel.application.request.duration</c> histogram (unit <c>s</c>) tagged with
    /// <c>request.type</c>, <c>request.kind</c>, <c>outcome</c> and, on a non-success,
    /// <c>error.type</c>. This method registers both by string name only via
    /// <see cref="TracerProviderBuilder.AddSource(string[])"/> and
    /// <see cref="MeterProviderBuilder.AddMeter(string[])"/>, because the owning types are
    /// <c>internal</c> to that assembly. No <c>ProjectReference</c> to any
    /// <c>SharedKernel.Application.*</c> package is added or needed.
    /// </para>
    /// <para>
    /// The histogram view applies <see cref="RequestDurationBucketBoundariesSeconds"/>. A service
    /// that needs different boundaries registers the meter and its own view directly instead of
    /// calling this method.
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

        // AddMeter de-duplicates by name, but a second AddView for the same instrument produces a
        // second metric stream, so the view is registered once per service collection.
        if (!builder.Services.Any(descriptor => descriptor.ServiceType == typeof(RequestDurationViewMarker)))
        {
            builder.Services.AddSingleton<RequestDurationViewMarker>();
            builder.Services.ConfigureOpenTelemetryMeterProvider(metrics => metrics.AddView(
                RequestDurationInstrumentName,
                new ExplicitBucketHistogramConfiguration { Boundaries = RequestDurationBucketBoundariesSeconds }));
        }

        return builder;
    }

    /// <summary>Records that the request-duration view has already been registered.</summary>
    private sealed class RequestDurationViewMarker;
}

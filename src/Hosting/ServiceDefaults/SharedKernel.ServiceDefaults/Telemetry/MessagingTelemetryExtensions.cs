using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace SharedKernel.ServiceDefaults.Telemetry;

/// <summary>
/// Wires MassTransit's own <c>"MassTransit"</c> <see cref="System.Diagnostics.ActivitySource"/> and
/// <see cref="System.Diagnostics.Metrics.Meter"/> — third-party instrumentation — into the host's
/// <c>TracerProvider</c>/<c>MeterProvider</c>.
/// </summary>
/// <remarks>
/// The kernel's own messaging telemetry (<c>SharedKernel.Messaging</c> and any sub-source) is exported by the
/// <c>SharedKernel.*</c> wildcard of <see cref="TelemetryExtensions.AddSharedKernelTelemetry"/> and needs no call.
/// </remarks>
public static class MessagingTelemetryExtensions
{
    // MassTransit's own name, emitted by the MassTransit library; renaming it upstream silently stops this export.
    private const string MassTransitInstrumentationName = "MassTransit";

    /// <summary>
    /// Adds the <c>"MassTransit"</c> <see cref="System.Diagnostics.ActivitySource"/> name to the host's
    /// <c>TracerProvider</c> and the <c>"MassTransit"</c> meter to the host's <c>MeterProvider</c>.
    /// </summary>
    /// <param name="builder">The host application builder.</param>
    /// <returns>The same <paramref name="builder"/> instance, for fluent chaining.</returns>
    /// <remarks>
    /// <para>
    /// <c>13.ServiceDefaults</c> does not create either; MassTransit emits both. This method only registers the names via
    /// <see cref="TracerProviderBuilder.AddSource(string[])"/> and <see cref="MeterProviderBuilder.AddMeter(string[])"/>.
    /// </para>
    /// <para>
    /// Idempotent: calling this method more than once on the same <paramref name="builder"/>
    /// registers no duplicate instrument — the underlying OpenTelemetry SDK no-ops when the same
    /// source/meter name is added more than once.
    /// </para>
    /// </remarks>
    public static IHostApplicationBuilder WithMessagingTelemetry(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services
            .AddOpenTelemetry()
            .WithTracing(tracing => tracing.AddSource(MassTransitInstrumentationName))
            .WithMetrics(metrics => metrics.AddMeter(MassTransitInstrumentationName));

        return builder;
    }
}

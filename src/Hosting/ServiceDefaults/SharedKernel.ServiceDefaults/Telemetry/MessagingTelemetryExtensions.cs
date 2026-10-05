using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace SharedKernel.ServiceDefaults.Telemetry;

/// <summary>
/// Wires the pre-existing <c>"MassTransit"</c> and <c>"SharedKernel.Messaging"</c>
/// <see cref="System.Diagnostics.ActivitySource"/> names — the latter owned and emitted by
/// <c>07.Messaging</c>'s <c>SharedKernel.Messaging.MassTransit.Diagnostics.MessagingDiagnostics</c>
/// — into the host's <c>TracerProvider</c>/<c>MeterProvider</c>.
/// </summary>
public static class MessagingTelemetryExtensions
{
    private const string MassTransitInstrumentationName = "MassTransit";
    private const string SharedKernelMessagingActivitySourceName = "SharedKernel.Messaging";

    /// <summary>
    /// Adds the <c>"MassTransit"</c> and <c>"SharedKernel.Messaging"</c>
    /// <see cref="System.Diagnostics.ActivitySource"/> names to the host's <c>TracerProvider</c>,
    /// and the <c>"MassTransit"</c> meter to the host's <c>MeterProvider</c>.
    /// </summary>
    /// <param name="builder">The host application builder.</param>
    /// <returns>The same <paramref name="builder"/> instance, for fluent chaining.</returns>
    /// <remarks>
    /// <para>
    /// <c>13.ServiceDefaults</c> does not create either source — <c>"MassTransit"</c> is emitted by
    /// the MassTransit library itself, and <c>"SharedKernel.Messaging"</c> is created and used
    /// entirely within <c>SharedKernel.Messaging.MassTransit</c>'s
    /// <c>ConsumerBase&lt;TMessage&gt;.Consume()</c> and
    /// <c>MassTransitEventPublisher.PublishAsync&lt;TEvent&gt;()</c>, which start child
    /// <see cref="System.Diagnostics.Activity"/> instances from it. This method only registers the
    /// already-existing source/meter names with the host's <c>TracerProvider</c>/<c>MeterProvider</c>
    /// via <see cref="TracerProviderBuilder.AddSource(string[])"/> and
    /// <see cref="MeterProviderBuilder.AddMeter(string[])"/>.
    /// </para>
    /// <para>
    /// Idempotent: calling this method more than once on the same <paramref name="builder"/>
    /// registers no duplicate instrument — the underlying OpenTelemetry SDK no-ops when the same
    /// source/meter name is added to a <c>TracerProviderBuilder</c>/<c>MeterProviderBuilder</c> more
    /// than once.
    /// </para>
    /// </remarks>
    public static IHostApplicationBuilder WithMessagingTelemetry(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services
            .AddOpenTelemetry()
            .WithTracing(tracing => tracing
                .AddSource(MassTransitInstrumentationName)
                .AddSource(SharedKernelMessagingActivitySourceName))
            .WithMetrics(metrics => metrics.AddMeter(MassTransitInstrumentationName));

        return builder;
    }
}

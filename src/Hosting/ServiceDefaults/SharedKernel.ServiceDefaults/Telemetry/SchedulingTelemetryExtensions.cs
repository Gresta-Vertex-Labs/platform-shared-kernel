using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace SharedKernel.ServiceDefaults.Telemetry;

/// <summary>
/// Wires the pre-existing <c>"SharedKernel.Scheduling"</c> <see cref="System.Diagnostics.ActivitySource"/>
/// and <see cref="System.Diagnostics.Metrics.Meter"/> — owned and emitted by <c>19.Scheduling</c>'s
/// <c>SharedKernel.Scheduling</c> package under its own internal diagnostics class — into the host's
/// <c>TracerProvider</c>/<c>MeterProvider</c>.
/// </summary>
public static class SchedulingTelemetryExtensions
{
    private const string SchedulingInstrumentationName = "SharedKernel.Scheduling";

    /// <summary>
    /// Adds the <c>"SharedKernel.Scheduling"</c> <see cref="System.Diagnostics.ActivitySource"/> name
    /// to the host's <c>TracerProvider</c>, and the <c>"SharedKernel.Scheduling"</c> meter to the
    /// host's <c>MeterProvider</c>.
    /// </summary>
    /// <param name="builder">The host application builder.</param>
    /// <returns>The same <paramref name="builder"/> instance, for fluent chaining.</returns>
    /// <remarks>
    /// <para>
    /// <b>Unlike <see cref="PersistenceTelemetryExtensions.WithPersistenceTelemetry"/>/
    /// <see cref="IntegrationTelemetryExtensions.WithIntegrationTelemetry"/>'s deliberate
    /// tracing-only shape, this method wires BOTH signals</b> — <c>19.Scheduling</c> ships a
    /// companion <c>Meter</c> alongside its <c>ActivitySource</c> (both named
    /// <c>"SharedKernel.Scheduling"</c>, confirmed directly against
    /// <c>SharedKernel.Scheduling/Diagnostics/SchedulingTelemetry.cs</c>), so omitting
    /// <c>WithMetrics(...)</c> here would be a real gap, not a documented scope decision — the same
    /// six-sibling shape as <see cref="MessagingTelemetryExtensions.WithMessagingTelemetry"/>/
    /// <see cref="CachingTelemetryExtensions.WithCachingTelemetry"/>/
    /// <see cref="ApplicationTelemetryExtensions.WithApplicationTelemetry"/>/
    /// <see cref="SearchTelemetryExtensions.WithSearchTelemetry"/>/
    /// <see cref="IntelligenceTelemetryExtensions.WithIntelligenceTelemetry"/>/
    /// <see cref="WorkflowTelemetryExtensions.WithWorkflowTelemetry"/>.
    /// </para>
    /// <para>
    /// <c>13.ServiceDefaults</c> does not create this source/meter pair — <c>19.Scheduling</c>'s
    /// <c>SharedKernel.Scheduling</c> package creates its own
    /// <see cref="System.Diagnostics.ActivitySource"/>/<see cref="System.Diagnostics.Metrics.Meter"/>
    /// instance, named from its own internal <c>SchedulingTelemetry.Name</c> constant (both
    /// <c>"SharedKernel.Scheduling"</c>). This method only registers the already-existing
    /// source/meter name with the host's <c>TracerProvider</c>/<c>MeterProvider</c> via
    /// <see cref="TracerProviderBuilder.AddSource(string[])"/> and
    /// <see cref="MeterProviderBuilder.AddMeter(string[])"/> — by string name only, so it needs
    /// <b>no reference</b> to <c>SharedKernel.Scheduling</c>, which is why it lives in this
    /// dependency-free base. Readiness needs no reference either: <c>SharedKernel.Scheduling</c>
    /// registers its own <c>IReadinessProbe</c>, which
    /// <see cref="HealthChecks.ReadinessHealthCheckExtensions.AddSharedKernelReadiness"/> maps onto
    /// <c>/health/ready</c> with every other provider's probe.
    /// </para>
    /// <para>
    /// Idempotent: calling this method more than once on the same <paramref name="builder"/>
    /// registers no duplicate instrument — the underlying OpenTelemetry SDK no-ops when the same
    /// source/meter name is added to a <c>TracerProviderBuilder</c>/<c>MeterProviderBuilder</c> more
    /// than once.
    /// </para>
    /// </remarks>
    public static IHostApplicationBuilder WithSchedulingTelemetry(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services
            .AddOpenTelemetry()
            .WithTracing(tracing => tracing.AddSource(SchedulingInstrumentationName))
            .WithMetrics(metrics => metrics.AddMeter(SchedulingInstrumentationName));

        return builder;
    }
}

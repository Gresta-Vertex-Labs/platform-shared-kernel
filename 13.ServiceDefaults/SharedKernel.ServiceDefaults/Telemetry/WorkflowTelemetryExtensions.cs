using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace SharedKernel.ServiceDefaults.Telemetry;

/// <summary>
/// Wires the pre-existing <c>"SharedKernel.Workflows"</c> <see cref="System.Diagnostics.ActivitySource"/>
/// and <see cref="System.Diagnostics.Metrics.Meter"/> — owned and emitted by <c>17.Workflows</c>'s
/// <c>SharedKernel.Workflows.Temporal</c> package under its own internal diagnostics class — into
/// the host's <c>TracerProvider</c>/<c>MeterProvider</c>.
/// </summary>
public static class WorkflowTelemetryExtensions
{
    private const string WorkflowInstrumentationName = "SharedKernel.Workflows";

    /// <summary>
    /// Adds the <c>"SharedKernel.Workflows"</c> <see cref="System.Diagnostics.ActivitySource"/> name
    /// to the host's <c>TracerProvider</c>, and the <c>"SharedKernel.Workflows"</c> meter to the
    /// host's <c>MeterProvider</c>.
    /// </summary>
    /// <param name="builder">The host application builder.</param>
    /// <returns>The same <paramref name="builder"/> instance, for fluent chaining.</returns>
    /// <remarks>
    /// <para>
    /// <c>13.ServiceDefaults</c> does not create this source/meter pair — <c>17.Workflows</c>'s
    /// <c>SharedKernel.Workflows.Temporal</c> package creates its own
    /// <see cref="System.Diagnostics.ActivitySource"/>/<see cref="System.Diagnostics.Metrics.Meter"/>
    /// instance, named from its own <c>WorkflowWellKnown.ActivitySourceName</c>/<c>.MeterName</c>
    /// constants (both <c>"SharedKernel.Workflows"</c>). This method only registers the
    /// already-existing source/meter name with the host's <c>TracerProvider</c>/<c>MeterProvider</c>
    /// via <see cref="TracerProviderBuilder.AddSource(string[])"/> and
    /// <see cref="MeterProviderBuilder.AddMeter(string[])"/> — by string name only. This is
    /// deliberately distinct from <see cref="HealthChecks.WorkflowReadinessHealthCheckExtensions"/>'s
    /// <see cref="HealthChecks.WorkflowReadinessHealthCheckExtensions.AddWorkflowReadinessCheck"/>,
    /// which DOES require a <c>ProjectReference</c> (to reach <c>IWorkflowServiceProbe</c>, a real
    /// type consumed by constructor injection, not just a string name) — this method needs no such
    /// reference, since a string name is all telemetry wiring requires.
    /// </para>
    /// <para>
    /// Idempotent: calling this method more than once on the same <paramref name="builder"/>
    /// registers no duplicate instrument — the underlying OpenTelemetry SDK no-ops when the same
    /// source/meter name is added to a <c>TracerProviderBuilder</c>/<c>MeterProviderBuilder</c> more
    /// than once.
    /// </para>
    /// </remarks>
    public static IHostApplicationBuilder WithWorkflowTelemetry(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services
            .AddOpenTelemetry()
            .WithTracing(tracing => tracing.AddSource(WorkflowInstrumentationName))
            .WithMetrics(metrics => metrics.AddMeter(WorkflowInstrumentationName));

        return builder;
    }
}

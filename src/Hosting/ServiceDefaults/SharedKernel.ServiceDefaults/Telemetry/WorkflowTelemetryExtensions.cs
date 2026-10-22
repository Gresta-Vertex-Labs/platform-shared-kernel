using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Trace;

namespace SharedKernel.ServiceDefaults.Telemetry;

/// <summary>
/// Wires the Temporal SDK's tracing sources — third-party instrumentation — into the host's <c>TracerProvider</c>.
/// </summary>
/// <remarks>
/// The kernel's own workflow telemetry, if any, is exported by the <c>SharedKernel.*</c> wildcard of
/// <see cref="TelemetryExtensions.AddSharedKernelTelemetry(Microsoft.Extensions.Hosting.IHostApplicationBuilder, string)"/> and needs no call.
/// </remarks>
public static class WorkflowTelemetryExtensions
{
    // The four ActivitySource names of Temporalio.Extensions.OpenTelemetry's TracingInterceptor (client, workflow,
    // activity, Nexus), read from the SDK's own source objects by the tests. They share this prefix; a source outside
    // it is not exported. An upstream rename fails WithWorkflowTelemetryTests.
    private const string TemporalTracingSources = "Temporalio.Extensions.OpenTelemetry.*";

    /// <summary>
    /// Adds the Temporal tracing sources (<c>Temporalio.Extensions.OpenTelemetry.*</c>: client, workflow, activity and
    /// Nexus) to the host's <c>TracerProvider</c>.
    /// </summary>
    /// <param name="builder">The host application builder.</param>
    /// <returns>The same <paramref name="builder"/> instance, for fluent chaining.</returns>
    /// <remarks>
    /// <para>
    /// The spans come from <c>TracingInterceptor</c>, which <c>SharedKernel.Workflows.Temporal</c> registers when
    /// tracing is enabled on its builder; this method only subscribes their names, so it needs no reference to that
    /// package. Readiness needs none either: the Temporal package registers its own <c>IReadinessProbe</c>, which
    /// <see cref="HealthChecks.ReadinessHealthCheckExtensions.AddSharedKernelReadiness"/> maps onto <c>/health/ready</c>.
    /// </para>
    /// <para>
    /// Idempotent: calling this method more than once on the same <paramref name="builder"/>
    /// registers no duplicate source — the underlying OpenTelemetry SDK no-ops when the same name is added more than once.
    /// </para>
    /// </remarks>
    public static IHostApplicationBuilder WithWorkflowTelemetry(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services
            .AddOpenTelemetry()
            .WithTracing(tracing => tracing.AddSource(TemporalTracingSources));

        return builder;
    }
}

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Trace;

namespace SharedKernel.ServiceDefaults.Telemetry;

/// <summary>
/// Wires the pre-existing <c>"SharedKernel.Integration"</c>
/// <see cref="System.Diagnostics.ActivitySource"/> — owned and emitted by <c>15.Integration</c>'s
/// <c>SharedKernel.Integration.Webhooks</c> package's outbound webhook dispatch operations — into
/// the host's <c>TracerProvider</c>.
/// </summary>
public static class IntegrationTelemetryExtensions
{
    private const string IntegrationInstrumentationName = "SharedKernel.Integration";

    /// <summary>
    /// Adds the <c>"SharedKernel.Integration"</c> <see cref="System.Diagnostics.ActivitySource"/>
    /// name to the host's <c>TracerProvider</c>.
    /// </summary>
    /// <param name="builder">The host application builder.</param>
    /// <returns>The same <paramref name="builder"/> instance, for fluent chaining.</returns>
    /// <remarks>
    /// <para>
    /// <c>13.ServiceDefaults</c> does not create this source — <c>15.Integration</c>'s
    /// <c>SharedKernel.Integration.Webhooks.Dispatch.WebhookIntegrationActivitySource</c> creates and
    /// owns it, name <c>"SharedKernel.Integration"</c>. This method only registers the
    /// already-existing source name with the host's <c>TracerProvider</c> via
    /// <see cref="TracerProviderBuilder.AddSource(string[])"/> — by string name only, since this
    /// domain deliberately takes no <c>ProjectReference</c> to
    /// <c>SharedKernel.Integration.Webhooks</c> for this purpose, mirroring
    /// <see cref="MessagingTelemetryExtensions.WithMessagingTelemetry"/>/
    /// <see cref="ApplicationTelemetryExtensions.WithApplicationTelemetry"/>/
    /// <see cref="PersistenceTelemetryExtensions.WithPersistenceTelemetry"/>'s string-name-only
    /// wiring exactly.
    /// </para>
    /// <para>
    /// <strong>Deliberately wires tracing only — there is no companion metrics call.</strong> Unlike
    /// most of its siblings (<see cref="MessagingTelemetryExtensions.WithMessagingTelemetry"/>,
    /// <see cref="ApplicationTelemetryExtensions.WithApplicationTelemetry"/>,
    /// <see cref="CachingTelemetryExtensions.WithCachingTelemetry"/>,
    /// <see cref="SearchTelemetryExtensions.WithSearchTelemetry"/>,
    /// <see cref="IntelligenceTelemetryExtensions.WithIntelligenceTelemetry"/>, and
    /// <see cref="WorkflowTelemetryExtensions.WithWorkflowTelemetry"/>, each of which wires both a
    /// tracing source and a metrics meter), <c>15.Integration</c> ships only an
    /// <see cref="System.Diagnostics.ActivitySource"/> for webhook-dispatch spans — it defines no
    /// companion <see cref="System.Diagnostics.Metrics.Meter"/> to wire (WO-064/P-424). This mirrors
    /// <see cref="PersistenceTelemetryExtensions.WithPersistenceTelemetry"/>'s identical tracing-only
    /// shape (D-16) exactly and is a deliberate scope decision recorded at design time (D-29), not an
    /// oversight — do not add a <c>WithMetrics(...)</c> call here unless <c>15.Integration</c> ships
    /// a corresponding meter in a future phase.
    /// </para>
    /// <para>
    /// Idempotent: calling this method more than once on the same <paramref name="builder"/>
    /// registers no duplicate instrument — the underlying OpenTelemetry SDK no-ops when the same
    /// source name is added to a <c>TracerProviderBuilder</c> more than once.
    /// </para>
    /// </remarks>
    public static IHostApplicationBuilder WithIntegrationTelemetry(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services
            .AddOpenTelemetry()
            .WithTracing(tracing => tracing.AddSource(IntegrationInstrumentationName));

        return builder;
    }
}

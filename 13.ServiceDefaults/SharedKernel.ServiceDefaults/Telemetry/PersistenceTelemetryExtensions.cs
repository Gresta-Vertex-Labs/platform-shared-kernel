using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Trace;

namespace SharedKernel.ServiceDefaults.Telemetry;

/// <summary>
/// Wires the pre-existing <c>"SharedKernel.Persistence"</c>
/// <see cref="System.Diagnostics.ActivitySource"/> — owned and emitted by <c>06.Persistence</c>'s
/// <c>SharedKernel.Persistence.EfCore</c> package's repository operations — into the host's
/// <c>TracerProvider</c>.
/// </summary>
public static class PersistenceTelemetryExtensions
{
    private const string PersistenceInstrumentationName = "SharedKernel.Persistence";

    /// <summary>
    /// Adds the <c>"SharedKernel.Persistence"</c> <see cref="System.Diagnostics.ActivitySource"/>
    /// name to the host's <c>TracerProvider</c>.
    /// </summary>
    /// <param name="builder">The host application builder.</param>
    /// <returns>The same <paramref name="builder"/> instance, for fluent chaining.</returns>
    /// <remarks>
    /// <para>
    /// <c>13.ServiceDefaults</c> does not create this source — <c>06.Persistence</c>'s
    /// <c>SharedKernel.Persistence.EfCore.Diagnostics.PersistenceActivitySource</c> creates and owns
    /// it, name/version <c>("SharedKernel.Persistence", "1.0")</c>. This method only registers the
    /// already-existing source name with the host's <c>TracerProvider</c> via
    /// <see cref="TracerProviderBuilder.AddSource(string[])"/> — by string name only, since this
    /// domain deliberately takes no <c>ProjectReference</c> to
    /// <c>SharedKernel.Persistence.EfCore</c> for this purpose (<c>PersistenceActivitySource</c> is
    /// <c>internal</c> with no <c>InternalsVisibleTo</c> grant to <c>SharedKernel.ServiceDefaults</c>),
    /// mirroring <see cref="MessagingTelemetryExtensions.WithMessagingTelemetry"/>/
    /// <see cref="CachingTelemetryExtensions.WithCachingTelemetry"/>'s string-name-only wiring
    /// exactly.
    /// </para>
    /// <para>
    /// <strong>Deliberately wires tracing only — there is no companion metrics call.</strong> Unlike
    /// its six siblings (<see cref="MessagingTelemetryExtensions.WithMessagingTelemetry"/>,
    /// <see cref="ApplicationTelemetryExtensions.WithApplicationTelemetry"/>,
    /// <see cref="CachingTelemetryExtensions.WithCachingTelemetry"/>,
    /// <see cref="SearchTelemetryExtensions.WithSearchTelemetry"/>,
    /// <see cref="IntelligenceTelemetryExtensions.WithIntelligenceTelemetry"/>, and
    /// <see cref="WorkflowTelemetryExtensions.WithWorkflowTelemetry"/>, each of which wires both a
    /// tracing source and a metrics meter), <c>06.Persistence</c> ships only an
    /// <see cref="System.Diagnostics.ActivitySource"/> for repository-operation spans — it defines no
    /// companion <see cref="System.Diagnostics.Metrics.Meter"/> to wire (WO-051/P-319). This is a
    /// deliberate scope decision recorded at design time (D-16), not an oversight — do not add a
    /// <c>WithMetrics(...)</c> call here unless <c>06.Persistence</c> ships a corresponding meter in a
    /// future phase.
    /// </para>
    /// <para>
    /// Idempotent: calling this method more than once on the same <paramref name="builder"/>
    /// registers no duplicate instrument — the underlying OpenTelemetry SDK no-ops when the same
    /// source name is added to a <c>TracerProviderBuilder</c> more than once.
    /// </para>
    /// </remarks>
    public static IHostApplicationBuilder WithPersistenceTelemetry(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services
            .AddOpenTelemetry()
            .WithTracing(tracing => tracing.AddSource(PersistenceInstrumentationName));

        return builder;
    }
}

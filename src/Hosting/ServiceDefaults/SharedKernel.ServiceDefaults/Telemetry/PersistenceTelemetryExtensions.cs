using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace SharedKernel.ServiceDefaults.Telemetry;

/// <summary>
/// Wires Npgsql's own <c>"Npgsql"</c> <see cref="System.Diagnostics.ActivitySource"/> and
/// <see cref="System.Diagnostics.Metrics.Meter"/> — third-party instrumentation — into the host's
/// <c>TracerProvider</c>/<c>MeterProvider</c>.
/// </summary>
/// <remarks>
/// The kernel's own persistence telemetry (<c>SharedKernel.Persistence</c> and its sub-sources) is exported by the
/// <c>SharedKernel.*</c> wildcard of <see cref="TelemetryExtensions.AddSharedKernelTelemetry(Microsoft.Extensions.Hosting.IHostApplicationBuilder, string)"/> and needs no call.
/// </remarks>
public static class PersistenceTelemetryExtensions
{
    // Npgsql's own well-known ActivitySource/Meter name. Npgsql emits on it only when the service additionally
    // references the third-party Npgsql.OpenTelemetry package and enables it on its NpgsqlDataSourceBuilder; the name
    // here is a no-op until that opt-in is made. Renaming it upstream silently stops this export.
    private const string NpgsqlInstrumentationName = "Npgsql";

    /// <summary>
    /// Adds the <c>"Npgsql"</c> <see cref="System.Diagnostics.ActivitySource"/> name to the host's
    /// <c>TracerProvider</c> and the <c>"Npgsql"</c> meter to the host's <c>MeterProvider</c>.
    /// </summary>
    /// <param name="builder">The host application builder.</param>
    /// <returns>The same <paramref name="builder"/> instance, for fluent chaining.</returns>
    /// <remarks>
    /// Idempotent: calling this method more than once on the same <paramref name="builder"/>
    /// registers no duplicate instrument — the underlying OpenTelemetry SDK no-ops when the same
    /// source/meter name is added more than once.
    /// </remarks>
    public static IHostApplicationBuilder WithPersistenceTelemetry(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services
            .AddOpenTelemetry()
            .WithTracing(tracing => tracing.AddSource(NpgsqlInstrumentationName))
            .WithMetrics(metrics => metrics.AddMeter(NpgsqlInstrumentationName));

        return builder;
    }
}

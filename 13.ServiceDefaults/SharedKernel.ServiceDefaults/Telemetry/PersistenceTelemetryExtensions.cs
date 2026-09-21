using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace SharedKernel.ServiceDefaults.Telemetry;

/// <summary>
/// Wires every pre-existing <c>06.Persistence</c> <see cref="System.Diagnostics.ActivitySource"/>/
/// <see cref="System.Diagnostics.Metrics.Meter"/> name — owned and emitted by
/// <c>SharedKernel.Persistence.EfCore</c>, <c>.Dapper</c>, <c>.EfCore.Encryption</c>, and
/// <c>.EfCore.Auditing</c> — plus Npgsql's own well-known instrumentation names, into the host's
/// <c>TracerProvider</c>/<c>MeterProvider</c>.
/// </summary>
public static class PersistenceTelemetryExtensions
{
    // Every name below is owned and created by its named 06.Persistence package, never by this
    // package — the ServiceDefaults base takes no SharedKernel package reference at all, so
    // every one of these is a literal string, kept in sync with the owning package's own public name
    // constant by convention (each owning type's XML doc calls out this coordination point
    // explicitly; renaming one without updating this file silently stops that source/meter exporting).

    private const string EfCoreInstrumentationName = "SharedKernel.Persistence";
    private const string DapperInstrumentationName = "SharedKernel.Persistence.Dapper";
    private const string EncryptionMeterInstrumentationName = "SharedKernel.Persistence.EfCore.Encryption";
    private const string AuditingMeterInstrumentationName = "SharedKernel.Persistence.EfCore.Auditing";

    // Npgsql's own well-known ActivitySource/Meter names (SharedKernel.Persistence.Npgsql.Telemetry
    // .NpgsqlTelemetryNames — not referenced by name here, since the ServiceDefaults base takes
    // no SharedKernel package reference). Npgsql only emits on these when a consumer additionally
    // references the third-party Npgsql.OpenTelemetry package and enables it on its NpgsqlDataSourceBuilder;
    // adding the name here is a no-op until that opt-in is made, mirroring this method's own
    // string-name-only wiring for SharedKernel.Persistence itself.
    private const string NpgsqlInstrumentationName = "Npgsql";

    /// <summary>
    /// Adds every <c>06.Persistence</c>-owned <see cref="System.Diagnostics.ActivitySource"/> name
    /// (<c>SharedKernel.Persistence.EfCore</c>'s repository-operation spans, <c>.Dapper</c>'s
    /// query/command spans) plus <c>"Npgsql"</c> to the host's <c>TracerProvider</c>, and every
    /// <c>06.Persistence</c>-owned <see cref="System.Diagnostics.Metrics.Meter"/> name
    /// (<c>SharedKernel.Persistence.EfCore</c>'s concurrency/tenant-isolation counters,
    /// <c>.EfCore.Encryption</c>'s encrypt/decrypt/rotation counters, <c>.EfCore.Auditing</c>'s
    /// append/verification instruments) plus <c>"Npgsql"</c> to the host's <c>MeterProvider</c>.
    /// </summary>
    /// <param name="builder">The host application builder.</param>
    /// <returns>The same <paramref name="builder"/> instance, for fluent chaining.</returns>
    /// <remarks>
    /// <para>
    /// <c>13.ServiceDefaults</c> does not create any of these sources/meters — each is created and
    /// owned entirely within its named <c>06.Persistence</c> package
    /// (<c>SharedKernel.Persistence.EfCore.Diagnostics.PersistenceActivitySource</c>/<c>.PersistenceMeter</c>,
    /// <c>SharedKernel.Persistence.Dapper.Diagnostics.DapperActivitySource</c>,
    /// <c>SharedKernel.Persistence.EfCore.Encryption.Diagnostics.EncryptionMeter</c>,
    /// <c>SharedKernel.Persistence.EfCore.Auditing.Diagnostics.AuditingMeter</c>). This method only
    /// registers the already-existing source/meter names with the host's
    /// <c>TracerProvider</c>/<c>MeterProvider</c> via <see cref="TracerProviderBuilder.AddSource(string[])"/>/
    /// <see cref="MeterProviderBuilder.AddMeter(string[])"/> — by string name only, since this domain
    /// deliberately takes no <c>ProjectReference</c> to any <c>06.Persistence</c> package for this
    /// purpose (every one of these types is <c>internal</c> or lives in a package this base
    /// deliberately never references), mirroring
    /// <see cref="MessagingTelemetryExtensions.WithMessagingTelemetry"/>/
    /// <see cref="CachingTelemetryExtensions.WithCachingTelemetry"/>'s string-name-only wiring
    /// exactly.
    /// </para>
    /// <para>
    /// The PostgreSQL setup in <c>SharedKernel.Persistence.EfCore</c> adds no <see cref="System.Diagnostics.ActivitySource"/>/
    /// <see cref="System.Diagnostics.Metrics.Meter"/> of its own to wire — its conventions/migration
    /// helpers remain build-time/migration-time only, and its one genuinely runtime type
    /// (<c>RowLevelSecurityConnectionInterceptor</c>) only logs, via <c>[LoggerMessage]</c>, which
    /// this method has nothing to do with (log export is wired separately, by
    /// <c>AddServiceDefaults</c>'s own OpenTelemetry logging pipeline).
    /// </para>
    /// <para>
    /// Idempotent: calling this method more than once on the same <paramref name="builder"/>
    /// registers no duplicate instrument — the underlying OpenTelemetry SDK no-ops when the same
    /// source/meter name is added to a <c>TracerProviderBuilder</c>/<c>MeterProviderBuilder</c> more
    /// than once.
    /// </para>
    /// </remarks>
    public static IHostApplicationBuilder WithPersistenceTelemetry(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services
            .AddOpenTelemetry()
            .WithTracing(tracing => tracing
                .AddSource(EfCoreInstrumentationName)
                .AddSource(DapperInstrumentationName)
                .AddSource(NpgsqlInstrumentationName))
            .WithMetrics(metrics => metrics
                .AddMeter(EfCoreInstrumentationName)
                .AddMeter(EncryptionMeterInstrumentationName)
                .AddMeter(AuditingMeterInstrumentationName)
                .AddMeter(NpgsqlInstrumentationName));

        return builder;
    }
}

namespace SharedKernel.Persistence.Npgsql.Telemetry;

/// <summary>
/// Well-known <see cref="System.Diagnostics.ActivitySource"/>/<see cref="System.Diagnostics.Metrics.Meter"/>
/// names Npgsql itself instruments under, published as compile-time constants so a host can wire them
/// into its <c>TracerProvider</c>/<c>MeterProvider</c> without retyping a literal.
/// </summary>
/// <remarks>
/// <para>
/// This package does not emit these instruments itself and does not reference the
/// <c>Npgsql.OpenTelemetry</c> package — Npgsql's own native OpenTelemetry integration does, when a
/// consumer adds that package and calls its data-source-builder <c>ConfigureTracing</c>/
/// <c>ConfigureMetrics</c> hook (reachable through
/// <c>NpgsqlPersistenceExtensions.AddSharedKernelNpgsql</c>'s <c>configureDataSource</c> callback).
/// This class exists purely so <c>13.ServiceDefaults</c>'s <c>WithPersistenceTelemetry</c> can add
/// the source/meter by name — see the name-only wiring precedent
/// <c>SharedKernel.Persistence.EfCore.Diagnostics.PersistenceActivitySource</c> already established
/// for <c>SharedKernel.Persistence</c> itself.
/// </para>
/// <para>
/// <strong>THIS EXACT NAME is the coordination point for <c>13.ServiceDefaults</c>'s
/// <c>WithPersistenceTelemetry</c></strong> — do not change without updating that consumer.
/// </para>
/// </remarks>
public static class NpgsqlTelemetryNames
{
    /// <summary>Npgsql's own <see cref="System.Diagnostics.ActivitySource"/> name.</summary>
    public const string ActivitySourceName = "Npgsql";

    /// <summary>Npgsql's own <see cref="System.Diagnostics.Metrics.Meter"/> name.</summary>
    public const string MeterName = "Npgsql";
}

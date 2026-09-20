using System.Diagnostics.Metrics;

namespace SharedKernel.Persistence.EfCore.Diagnostics;

/// <summary>
/// The shared <see cref="Meter"/> and instrument-name constants for
/// <c>SharedKernel.Persistence.EfCore</c> metrics.
/// </summary>
/// <remarks>
/// <para>
/// Mirrors <see cref="PersistenceActivitySource"/>'s "one shared, well-known name" precedent for
/// metrics. BCL <see cref="System.Diagnostics.Metrics.Meter"/> — zero new NuGet dependency, AOT-safe.
/// </para>
/// <para>
/// <strong>THIS EXACT NAME IS THE COORDINATION POINT for <c>13.ServiceDefaults</c>'s
/// <c>WithPersistenceTelemetry</c></strong> — do not rename without updating that consumer, which
/// calls <c>AddMeter(PersistenceMeter.Name)</c> (or the equivalent literal, kept in sync) to enable
/// OpenTelemetry export for this Meter. The individual instrument-name constants below let that
/// consumer (or any other) reference an instrument by name without re-declaring the string.
/// </para>
/// </remarks>
public static class PersistenceMeter
{
    /// <summary>The shared <see cref="Meter"/> name for this domain.</summary>
    public const string Name = "SharedKernel.Persistence";

    /// <summary>
    /// Counter name: the number of <see cref="Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException"/>
    /// occurrences <c>ConcurrencyInterceptor</c> translated into a typed
    /// <see cref="SharedKernel.Core.Exceptions.ConflictException"/>. Tagged with
    /// <see cref="PersistenceTagKeys.AggregateType"/>.
    /// </summary>
    public const string ConcurrencyConflictsTotal = "persistence.concurrency_conflicts";

    /// <summary>
    /// Counter name: the number of writes <c>TenantWriteGuardInterceptor</c> rejected for crossing
    /// tenant isolation. Tagged with <see cref="PersistenceTagKeys.AggregateType"/>.
    /// </summary>
    public const string TenantIsolationViolationsTotal = "persistence.tenant_isolation_violations";

    /// <summary>The shared <see cref="Meter"/> instance every instrument below is created from.</summary>
    internal static readonly Meter Instance = new(Name, "1.0");

    /// <summary>See <see cref="ConcurrencyConflictsTotal"/>.</summary>
    internal static readonly Counter<long> ConcurrencyConflicts =
        Instance.CreateCounter<long>(ConcurrencyConflictsTotal, unit: "{conflict}",
            description: "Concurrency conflicts translated by ConcurrencyInterceptor.");

    /// <summary>See <see cref="TenantIsolationViolationsTotal"/>.</summary>
    internal static readonly Counter<long> TenantIsolationViolations =
        Instance.CreateCounter<long>(TenantIsolationViolationsTotal, unit: "{violation}",
            description: "Writes rejected by TenantWriteGuardInterceptor for crossing tenant isolation.");
}

using System.Diagnostics.Metrics;
using SharedKernel.Persistence.Abstractions.Diagnostics;
using SharedKernel.Primitives.Telemetry;

namespace SharedKernel.Persistence.EfCore.Diagnostics;

/// <summary>
/// The one <see cref="Meter"/> of the EF Core, auditing and field-encryption packages, named
/// <see cref="PersistenceTelemetry.Name"/>, and the instruments of the save pipeline.
/// </summary>
/// <remarks>
/// <para>
/// The meter is created statically rather than through <c>IMeterFactory</c>: the instruments are recorded from the
/// static write guard and from pooled contexts built outside the container, neither of which can be handed a factory.
/// <c>AddServiceDefaults</c> subscribes to <c>SharedKernel.*</c> by name, so a host needs no registration.
/// </para>
/// <para>Instruments are named <c>sharedkernel.persistence.*</c> and carry a unit.</para>
/// </remarks>
internal static class PersistenceMeter
{
    /// <summary>The meter and source name, <see cref="PersistenceTelemetry.Name"/>, declared once more in this assembly.</summary>
    /// <remarks>
    /// The name-ownership check of the platform report looks for a <c>public const</c> of the name in the assembly that
    /// creates the meter; the type stays internal.
    /// </remarks>
    public const string Name = PersistenceTelemetry.Name;

    /// <summary>Counter name: concurrency conflicts the save pipeline translated into a <c>ConflictException</c>.</summary>
    public const string ConcurrencyConflictsInstrument = "sharedkernel.persistence.concurrency_conflicts";

    /// <summary>Counter name: writes the tenant write guard rejected, or the database proved, for crossing tenant isolation.</summary>
    public const string TenantIsolationViolationsInstrument = "sharedkernel.persistence.tenant_isolation_violations";

    /// <summary>The shared <see cref="Meter"/> every instrument of the three packages is created from.</summary>
    internal static readonly Meter Instance = new(
        PersistenceTelemetry.Name,
        TelemetryVersion.Of(typeof(PersistenceMeter).Assembly));

    /// <summary>See <see cref="ConcurrencyConflictsInstrument"/>. Tagged with <see cref="PersistenceTagKeys.AggregateType"/>.</summary>
    internal static readonly Counter<long> ConcurrencyConflicts =
        Instance.CreateCounter<long>(ConcurrencyConflictsInstrument, unit: "{conflict}",
            description: "Concurrency conflicts translated into ConflictException.");

    /// <summary>See <see cref="TenantIsolationViolationsInstrument"/>. Tagged with <see cref="PersistenceTagKeys.AggregateType"/>.</summary>
    internal static readonly Counter<long> TenantIsolationViolations =
        Instance.CreateCounter<long>(TenantIsolationViolationsInstrument, unit: "{violation}",
            description: "Writes rejected for crossing tenant isolation.");
}

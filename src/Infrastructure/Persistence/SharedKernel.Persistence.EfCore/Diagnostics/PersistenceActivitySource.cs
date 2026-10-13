using System.Diagnostics;
using SharedKernel.Persistence.Abstractions.Diagnostics;
using SharedKernel.Primitives.Telemetry;

namespace SharedKernel.Persistence.EfCore.Diagnostics;

/// <summary>
/// The one <see cref="ActivitySource"/> of the persistence packages, named <see cref="PersistenceTelemetry.Name"/>:
/// repository spans (<c>persistence {operation}</c>) and, through <c>InternalsVisibleTo</c>, the audit spans of
/// <c>SharedKernel.Persistence.EfCore.Auditing</c> (<c>persistence audit.{operation}</c>).
/// </summary>
/// <remarks>
/// Spans are <see cref="ActivityKind.Internal"/>: the <c>Client</c> span for the database call is the one Npgsql emits,
/// and a repository or ledger call is in-process work around it. Failures are recorded through the shared Primitives
/// recorder (<c>RecordException</c>/<c>RecordResult</c>). A tag never carries a tenant, an actor, a key or a value.
/// </remarks>
internal static class PersistenceActivitySource
{
    /// <summary>The shared <see cref="ActivitySource"/> instance for the domain.</summary>
    public static readonly ActivitySource Source = new(
        PersistenceTelemetry.Name,
        TelemetryVersion.Of(typeof(PersistenceActivitySource).Assembly));
}

using System.Diagnostics.Metrics;
using SharedKernel.Execution.Context;
using SharedKernel.Persistence.Abstractions.Diagnostics;
using SharedKernel.Primitives.Telemetry;

namespace SharedKernel.Persistence.Abstractions.Context;

/// <summary>Metrics recording every <see cref="ICrossTenantScope.Enter(string)"/> call.</summary>
/// <remarks>
/// Published on the meter <see cref="PersistenceTelemetry.Name"/>, the name the EF Core, auditing and encryption
/// packages use too, so one <c>AddMeter</c> exports them all. The meter is created statically: the scope is built by
/// the container, by <c>ICallerDbContextFactory</c> and by tests, none of which can be handed a meter factory.
/// </remarks>
internal static class CrossTenantScopeDiagnostics
{
    /// <summary>Tag key: the <see cref="ActorKind"/> of the caller that entered the bypass.</summary>
    internal const string ActorKindTag = "persistence.actor_kind";

    /// <summary>Counter name: the number of times a cross-tenant bypass was entered.</summary>
    internal const string ScopeEntriesInstrument = "sharedkernel.persistence.cross_tenant_scope_entries";

    private static readonly Meter Meter = new(PersistenceTelemetry.Name, TelemetryVersion.Of(typeof(CrossTenantScopeDiagnostics).Assembly));

    /// <summary>Incremented once per <see cref="ICrossTenantScope.Enter(string)"/> call.</summary>
    internal static readonly Counter<long> ScopeEntries = Meter.CreateCounter<long>(
        ScopeEntriesInstrument,
        unit: "{entry}",
        description: "Times a cross-tenant bypass was entered, tagged with the entering actor's kind.");
}

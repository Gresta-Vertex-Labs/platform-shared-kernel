using System.Diagnostics.Metrics;

namespace SharedKernel.Persistence.Abstractions.Context;

/// <summary>
/// Metrics recording every activation of an <see cref="ICrossTenantScope"/> bypass.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately metrics-only, not logging: <see cref="CrossTenantScope"/> is a process-wide
/// singleton, and this package stays free of any DI-resolved <c>ILogger</c>
/// consumer — the same reason <see cref="CrossTenantScope.Enter(string?)"/> takes the entering actor
/// as an explicit parameter rather than capturing a request-scoped identity context in its
/// constructor. BCL <see cref="Meter"/> needs no DI resolution and no new NuGet dependency.
/// </para>
/// <para>
/// The instrument is published on the meter name <c>"SharedKernel.Persistence"</c> — the exact same
/// name <c>SharedKernel.Persistence.EfCore</c>'s own metrics use — so both are exported by a single
/// <c>AddMeter("SharedKernel.Persistence")</c> call with no further coordination. A lower layer
/// cannot reference that higher layer's constant directly, so the literal is intentionally repeated
/// here rather than shared.
/// </para>
/// </remarks>
internal static class CrossTenantScopeDiagnostics
{
    private const string MeterName = "SharedKernel.Persistence";

    /// <summary>Tag key: the actor id supplied to <see cref="CrossTenantScope.Enter(string?)"/>, or <c>"unknown"</c> when omitted.</summary>
    internal const string ActorIdTag = "persistence.actor_id";

    /// <summary>Counter name: the number of times an <see cref="ICrossTenantScope"/> bypass was entered.</summary>
    internal const string ScopeEntriesTotal = "persistence.cross_tenant_scope_entries";

    private static readonly Meter Meter = new(MeterName, "1.0");

    /// <summary>Incremented once per <see cref="CrossTenantScope.Enter(string?)"/> call, tagged with the actor.</summary>
    internal static readonly Counter<long> ScopeEntries = Meter.CreateCounter<long>(
        ScopeEntriesTotal,
        unit: "{entry}",
        description: "Times an ICrossTenantScope bypass was entered, tagged with the entering actor when known.");
}

using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;
using SharedKernel.Application.Context;
using SharedKernel.Primitives.Logging;

namespace SharedKernel.Persistence.Abstractions.Context;

/// <summary>Metrics recording every <see cref="ICrossTenantScope.Enter(string)"/> call.</summary>
/// <remarks>
/// Published on the meter name <c>"SharedKernel.Persistence"</c>, the name <c>SharedKernel.Persistence.EfCore</c>'s
/// own metrics use, so one <c>AddMeter("SharedKernel.Persistence")</c> exports both. A lower layer cannot
/// reference the higher layer's constant, so the literal is repeated here.
/// </remarks>
internal static class CrossTenantScopeDiagnostics
{
    private const string MeterName = "SharedKernel.Persistence";

    /// <summary>Tag key: the <see cref="ActorKind"/> of the caller that entered the bypass.</summary>
    internal const string ActorKindTag = "persistence.actor_kind";

    /// <summary>Counter name: the number of times a cross-tenant bypass was entered.</summary>
    internal const string ScopeEntriesTotal = "persistence.cross_tenant_scope_entries";

    private static readonly Meter Meter = new(MeterName, "1.0");

    /// <summary>Incremented once per <see cref="ICrossTenantScope.Enter(string)"/> call.</summary>
    internal static readonly Counter<long> ScopeEntries = Meter.CreateCounter<long>(
        ScopeEntriesTotal,
        unit: "{entry}",
        description: "Times a cross-tenant bypass was entered, tagged with the entering actor's kind.");
}

/// <summary>
/// <c>[LoggerMessage]</c> statements of <c>SharedKernel.Persistence.Abstractions</c>: EventId sub-block
/// <c>6150-6199</c> of the <c>06.Persistence</c> range.
/// </summary>
internal static partial class CrossTenantScopeLog
{
    /// <summary>Logged for every cross-tenant bypass entry.</summary>
    [LoggerMessage(
        EventId = LoggingEventIdRanges.Persistence + 150,
        Level = LogLevel.Information,
        Message = "Cross-tenant scope entered by '{ActorId}' ({ActorKind}, tenant '{ActorTenantId}'): {Reason}")]
    internal static partial void Entered(
        ILogger logger,
        string actorId,
        ActorKind actorKind,
        Guid? actorTenantId,
        string reason);
}

using Microsoft.Extensions.Logging;
using SharedKernel.Execution.Context;

namespace SharedKernel.Persistence.Abstractions.Diagnostics;

/// <summary>
/// Log statements of <c>SharedKernel.Persistence.Abstractions</c>. No actor, tenant or caller-supplied text reaches a
/// log: the audit ledger records who acted.
/// </summary>
internal static partial class AbstractionsLog
{
    /// <summary>Logged for every cross-tenant bypass entry.</summary>
    [LoggerMessage(
        EventId = AbstractionsLoggingEventIds.CrossTenantScopeEntered,
        Level = LogLevel.Information,
        Message = "A cross-tenant scope was entered by a {ActorKind} actor.")]
    public static partial void CrossTenantScopeEntered(ILogger logger, ActorKind actorKind);
}

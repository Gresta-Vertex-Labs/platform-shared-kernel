using Microsoft.Extensions.Logging;
using SharedKernel.Primitives.Logging;

namespace SharedKernel.Persistence.EfCore.Diagnostics;

/// <summary>
/// <c>[LoggerMessage]</c> statements added by P-558 wave 2 (context, save pipeline, registration). Shares
/// <see cref="PersistenceLog"/>'s <c>6000-6099</c> sub-block and continues after its last id (6013).
/// </summary>
internal static partial class PersistenceContextLog
{
    /// <summary>Domain events were pending at save time but no <c>IDomainEventDispatcher</c> is attached.</summary>
    [LoggerMessage(
        EventId = LoggingEventIdRanges.Persistence + 14,
        Level = LogLevel.Warning,
        Message = "Discarded {EventCount} domain event(s) ({EventTypes}) because no IDomainEventDispatcher is registered. "
            + "Register one, or stop raising events that nothing handles.")]
    internal static partial void DomainEventsDiscarded(ILogger logger, int eventCount, string eventTypes);

    /// <summary>A write was rejected because the target row provably belongs to another tenant.</summary>
    [LoggerMessage(
        EventId = LoggingEventIdRanges.Persistence + 15,
        Level = LogLevel.Warning,
        Message = "Rejected a write to '{EntityType}': the targeted row belongs to another tenant.")]
    internal static partial void TenantIsolationViolationProven(ILogger logger, string entityType);

    /// <summary>Startup: the context's aggregates can raise domain events but no dispatcher is registered.</summary>
    [LoggerMessage(
        EventId = LoggingEventIdRanges.Persistence + 16,
        Level = LogLevel.Warning,
        Message = "No IDomainEventDispatcher is registered for '{ContextType}'. Domain events raised by its aggregates "
            + "will be discarded (and logged) at save time.")]
    internal static partial void NoDomainEventDispatcherRegistered(ILogger logger, string contextType);

    /// <summary>The current row version could not be read after a concurrency conflict.</summary>
    [LoggerMessage(
        EventId = LoggingEventIdRanges.Persistence + 17,
        Level = LogLevel.Debug,
        Message = "Could not read the current row of '{EntityType}' after a concurrency conflict.")]
    internal static partial void ConflictRowLookupFailed(ILogger logger, Exception exception, string entityType);

    /// <summary>Startup: the model of the context was built and validated.</summary>
    [LoggerMessage(
        EventId = LoggingEventIdRanges.Persistence + 18,
        Level = LogLevel.Debug,
        Message = "Model of '{ContextType}' validated at startup ({EntityTypeCount} entity types).")]
    internal static partial void ModelValidated(ILogger logger, string contextType, int entityTypeCount);
}

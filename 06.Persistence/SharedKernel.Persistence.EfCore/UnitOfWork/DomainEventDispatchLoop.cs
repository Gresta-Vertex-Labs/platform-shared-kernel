using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Domain.Events;
using SharedKernel.Persistence.EfCore.Diagnostics;

namespace SharedKernel.Persistence.EfCore.UnitOfWork;

/// <summary>
/// Pre-save domain-event dispatch, run by <c>SharedKernelDbContext.SaveChangesAsync</c> for every save path.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Dispatch until quiescent, then save once.</strong> Each pass collects the pending events of every
/// tracked <see cref="IHasDomainEvents"/> entity, clears them immediately (so a handler failure can never
/// make a later save dispatch them again) and dispatches the batch. A handler may raise more events or change
/// more tracked entities, so the loop repeats until a pass finds nothing. The single physical save that
/// follows persists the original change and every handler change atomically, and the events of an aggregate
/// deleted in the same save are captured before EF stops tracking it.
/// </para>
/// <para>
/// <strong>External side effects do not belong in these handlers.</strong> They run before the commit, so an
/// HTTP call or a direct message publish would happen even if the save later fails. Use
/// <c>ICommandScope.OnCompleted</c> (after the outermost commit) or a transactional outbox.
/// </para>
/// <para>
/// <strong>No dispatcher registered:</strong> the events are cleared and each discarded batch is logged as a
/// Warning (EventId 6014), so a service that forgot to register <see cref="IDomainEventDispatcher"/> notices.
/// </para>
/// </remarks>
internal static class DomainEventDispatchLoop
{
    /// <summary>The maximum number of passes before the loop gives up and throws.</summary>
    public const int MaxIterations = 100;

    /// <summary>Returns whether any tracked entity holds a pending domain event.</summary>
    public static bool HasPendingEvents(DbContext dbContext) =>
        dbContext.ChangeTracker.Entries<IHasDomainEvents>().Any(e => e.Entity.DomainEvents.Count > 0);

    /// <summary>Dispatches until quiescent, or discards with a warning when <paramref name="dispatcher"/> is null.</summary>
    /// <exception cref="InvalidOperationException">Quiescence was not reached within <see cref="MaxIterations"/> passes.</exception>
    public static async Task RunAsync(
        DbContext dbContext,
        IDomainEventDispatcher? dispatcher,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (dispatcher is null)
        {
            DiscardWithoutDispatcher(dbContext, logger);
            return;
        }

        for (var iteration = 0; iteration < MaxIterations; iteration++)
        {
            var batch = CollectAndClear(dbContext);
            if (batch.Count == 0)
                return;

            await dispatcher.DispatchAsync(batch, cancellationToken);
        }

        throw new InvalidOperationException(
            $"Domain-event dispatch did not reach quiescence after {MaxIterations} passes — a handler is " +
            "unconditionally re-raising events. This is almost always an application bug.");
    }

    /// <summary>Clears every pending event and logs how many were discarded.</summary>
    public static void DiscardWithoutDispatcher(DbContext dbContext, ILogger logger)
    {
        var batch = CollectAndClear(dbContext);
        if (batch.Count == 0)
            return;

        PersistenceContextLog.DomainEventsDiscarded(
            logger,
            batch.Count,
            string.Join(", ", batch.Select(e => e.GetType().Name).Distinct(StringComparer.Ordinal)));
    }

    private static List<IDomainEvent> CollectAndClear(DbContext dbContext)
    {
        var batch = new List<IDomainEvent>();
        foreach (var entry in dbContext.ChangeTracker.Entries<IHasDomainEvents>().ToList())
        {
            var aggregate = entry.Entity;
            if (aggregate.DomainEvents.Count == 0)
                continue;

            batch.AddRange(aggregate.DomainEvents);
            aggregate.ClearDomainEvents();
        }

        return batch;
    }
}

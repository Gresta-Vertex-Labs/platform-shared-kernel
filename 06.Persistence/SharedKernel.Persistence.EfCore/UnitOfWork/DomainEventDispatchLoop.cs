using Microsoft.EntityFrameworkCore;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Domain.Events;

namespace SharedKernel.Persistence.EfCore.UnitOfWork;

/// <summary>
/// Shared pre-commit domain-event dispatch logic for <see cref="EfUnitOfWork"/> and
/// <see cref="EfTransactionalUnitOfWork"/>.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Pre-commit, dispatch-until-quiescent:</strong> the pre-W2 design
/// collected and dispatched domain events AFTER <c>SaveChangesAsync</c> already succeeded, which had
/// three confirmed defects: (1) an aggregate that was HARD-deleted in the same save is no longer
/// tracked once the save succeeds, so any event it raised was silently lost; (2) if a dispatch
/// handler threw, the already-committed save could not be undone, but the events were also never
/// cleared, so a caller that reused the same aggregate instance and saved again would dispatch the
/// SAME events a second time; (3) a dispatch handler that itself wrote to the SAME
/// <see cref="DbContext"/> (e.g. updating a read model) had no further <c>SaveChangesAsync</c> call
/// to persist through — its writes were silently dropped.
/// </para>
/// <para>
/// The fix moves dispatch BEFORE the actual database save: <see cref="RunAsync"/> repeatedly scans
/// <see cref="ChangeTracker.Entries{TEntity}()"/> for <see cref="IHasDomainEvents"/> entities with
/// pending events, clears each aggregate's events immediately (so a handler-thrown exception can
/// never cause a later, unrelated save to redispatch the same events), and dispatches the batch —
/// then loops, because a handler may itself raise further events (directly, or indirectly by writing
/// to another tracked aggregate). The loop ends once a full pass finds nothing pending ("quiescent"),
/// after which exactly ONE physical <c>SaveChangesAsync</c> call persists the original change plus
/// every handler-caused change, all as a single atomic unit — a handler write is no longer dropped,
/// because it is now tracked before the save ever runs, and a hard-deleted aggregate's events were
/// captured before EF stopped tracking it.
/// </para>
/// <para>
/// <strong>External side effects do not belong here.</strong> Because dispatch now runs BEFORE the
/// database commit, a handler with a genuinely external effect (an outbound HTTP call, publishing to
/// <c>07.Messaging</c> outside its transactional outbox) would fire even if the eventual
/// <c>SaveChangesAsync</c> later fails and rolls back. An <see cref="IDomainEventDispatcher"/>
/// registered here must only drive handlers that write to the SAME <see cref="DbContext"/> (so their
/// effects roll back atomically with everything else). A handler with a real external effect
/// belongs in <c>05.Application.Behaviors</c>'s <c>ICommandScope.OnCompleted</c> (which only runs
/// after the OUTERMOST command has actually committed) or a transactional outbox
/// (<c>07.Messaging</c>'s MassTransit EF Core outbox integration) — never this dispatcher.
/// </para>
/// <para>
/// A bounded iteration cap (<see cref="MaxIterations"/>) guards against a genuine event-storm
/// application bug turning into an unbounded loop/stack of dispatch calls.
/// </para>
/// </remarks>
internal static class DomainEventDispatchLoop
{
    /// <summary>
    /// The maximum number of dispatch passes before <see cref="RunAsync"/> gives up and throws —
    /// guards against a handler that unconditionally re-raises a domain event turning into an
    /// effectively infinite loop.
    /// </summary>
    public const int MaxIterations = 100;

    /// <summary>
    /// Runs the pre-commit dispatch-until-quiescent loop against <paramref name="dbContext"/>.
    /// </summary>
    /// <param name="dbContext">The context whose change tracker is scanned for pending events.</param>
    /// <param name="dispatcher">
    /// The dispatcher to invoke, or <see langword="null"/> when none is registered — in that case
    /// every pending aggregate's events are still cleared (so they are never silently carried into a
    /// later, unrelated save) but nothing is dispatched.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the loop does not reach quiescence within <see cref="MaxIterations"/> passes.
    /// </exception>
    public static async Task RunAsync(
        DbContext dbContext,
        IDomainEventDispatcher? dispatcher,
        CancellationToken cancellationToken)
    {
        for (var iteration = 0; iteration < MaxIterations; iteration++)
        {
            var pendingAggregates = dbContext.ChangeTracker
                .Entries<IHasDomainEvents>()
                .Select(e => e.Entity)
                .Where(e => e.DomainEvents.Count > 0)
                .ToList();

            if (pendingAggregates.Count == 0)
                return;

            var batch = new List<IDomainEvent>();
            foreach (var aggregate in pendingAggregates)
            {
                batch.AddRange(aggregate.DomainEvents);
                // Clear immediately — before dispatch, not after — so a handler exception can never
                // leave events staged for a later, unrelated save to redispatch (H-A3).
                aggregate.ClearDomainEvents();
            }

            if (dispatcher is not null && batch.Count > 0)
                await dispatcher.DispatchAsync(batch, cancellationToken);
        }

        throw new InvalidOperationException(
            $"Domain-event dispatch did not reach quiescence after {MaxIterations} passes — a " +
            "handler is unconditionally re-raising events. This is almost always an application bug " +
            "(an event handler that always raises the same or an equivalent event) rather than a " +
            "genuinely unbounded cascade.");
    }
}

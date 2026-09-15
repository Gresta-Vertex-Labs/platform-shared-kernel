using SharedKernel.Domain.Events;

namespace SharedKernel.Domain.Abstractions;

/// <summary>
/// Delivers the domain events collected from saved aggregates to their handlers.
/// </summary>
/// <remarks>
/// <para>
/// <b>Contract.</b> An implementation treats an empty list as a no-op and lets a handler's exception
/// propagate to the caller unchanged: never swallowed, wrapped, or logged and suppressed.
/// </para>
/// <para>
/// <b>Registration.</b> This package ships no implementation and nothing registers one automatically.
/// The consuming service registers one, typically a MediatR-based dispatcher from the application layer.
/// </para>
/// <para>
/// <b>Pitfall.</b> The persistence layer treats the dispatcher as optional. When none is registered it
/// still clears the pending events after saving, so they are lost without any error.
/// </para>
/// </remarks>
public interface IDomainEventDispatcher
{
    /// <summary>
    /// Dispatches <paramref name="events"/> to their handlers.
    /// </summary>
    /// <param name="events">
    /// The events to dispatch. Must not be <see langword="null"/>; an empty list is a no-op.
    /// </param>
    /// <param name="cancellationToken">A token that cancels the dispatch.</param>
    /// <returns>
    /// A task that completes when every event has been dispatched, or immediately when
    /// <paramref name="events"/> is empty.
    /// </returns>
    Task DispatchAsync(IReadOnlyList<IDomainEvent> events, CancellationToken cancellationToken);
}

using SharedKernel.Domain.Events;

namespace SharedKernel.Domain.Abstractions;

/// <summary>
/// A type that holds the domain events it has raised until infrastructure dispatches them.
/// </summary>
/// <remarks>
/// <para>
/// <b>Dispatch.</b> Infrastructure that collects and dispatches events, such as a unit of work or an
/// outbox publisher, depends on this interface rather than <see cref="IAggregateRoot{TId}"/>, because
/// dispatch needs no identity type.
/// </para>
/// <para>
/// <b>Clearing.</b> Only infrastructure calls <see cref="ClearDomainEvents"/>, after saving and
/// dispatching. Aggregates and application code never call it.
/// </para>
/// </remarks>
public interface IHasDomainEvents
{
    /// <summary>
    /// Gets the domain events raised since the last call to <see cref="ClearDomainEvents"/>, in the order
    /// they were raised.
    /// </summary>
    IReadOnlyCollection<IDomainEvent> DomainEvents { get; }

    /// <summary>
    /// Removes all pending domain events.
    /// </summary>
    /// <remarks>
    /// Called only by infrastructure after dispatch. It does not change the event sequence number,
    /// <see cref="IHasVersion.Version"/>.
    /// </remarks>
    void ClearDomainEvents();
}

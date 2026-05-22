using SharedKernel.Domain.Events;

namespace SharedKernel.Domain.Abstractions;

/// <summary>
/// Marks a type as a DDD aggregate root — the consistency boundary for a cluster of domain objects.
/// Extends <see cref="IEntity{TId}"/> and provides access to the domain events raised during the
/// current unit of work.
/// </summary>
/// <typeparam name="TId">The type of the aggregate's identity key. Must be non-null.</typeparam>
public interface IAggregateRoot<TId> : IEntity<TId> where TId : notnull
{
    /// <summary>
    /// Gets the domain events raised by this aggregate since the last call to
    /// <see cref="ClearDomainEvents"/>. Infrastructure clears this collection after successful dispatch.
    /// </summary>
    IReadOnlyCollection<IDomainEvent> DomainEvents { get; }

    /// <summary>
    /// Clears all domain events from this aggregate's internal collection.
    /// </summary>
    /// <remarks>
    /// Called exclusively by infrastructure dispatch code after events have been successfully published.
    /// Aggregates and application services must never call this method directly.
    /// </remarks>
    void ClearDomainEvents();
}

using SharedKernel.Domain.Events;

namespace SharedKernel.Domain.Abstractions;

/// <summary>
/// Represents a type that accumulates domain events during the current unit of work.
/// </summary>
/// <remarks>
/// <para>
/// Infrastructure dispatch code (e.g., EF Core interceptors, outbox publishers) must depend on
/// <see cref="IHasDomainEvents"/> rather than <c>IAggregateRoot&lt;TId&gt;</c>. Aggregate identity
/// is not required to dispatch domain events — keeping event dispatch decoupled from the aggregate's
/// identity type makes dispatch infrastructure simpler and more reusable.
/// </para>
/// <para>
/// <see cref="ClearDomainEvents"/> is called by infrastructure after events have been
/// successfully dispatched. Aggregates and application services must never call it directly.
/// </para>
/// </remarks>
public interface IHasDomainEvents
{
    /// <summary>
    /// Gets the domain events raised by this aggregate since the last call to
    /// <see cref="ClearDomainEvents"/>. Infrastructure clears this collection after successful dispatch.
    /// </summary>
    IReadOnlyCollection<IDomainEvent> DomainEvents { get; }

    /// <summary>
    /// Clears all domain events from the internal collection.
    /// </summary>
    /// <remarks>
    /// Called exclusively by infrastructure dispatch code after events have been successfully published.
    /// Aggregates and application services must never call this method directly.
    /// </remarks>
    void ClearDomainEvents();
}

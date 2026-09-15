namespace SharedKernel.Domain.Abstractions;

/// <summary>
/// An aggregate root: the entity that guards the consistency boundary of a cluster of domain objects
/// and records the domain events raised inside it.
/// </summary>
/// <typeparam name="TId">The identity key type. Must be non-null.</typeparam>
/// <remarks>
/// <para>
/// <b>Usage.</b> Extend <see cref="SharedKernel.Domain.Aggregates.AggregateRoot{TId}"/> or one of its
/// audit, soft-delete and tenant variants rather than implementing this interface directly; the base
/// class supplies event recording, the clock and the event sequence number.
/// </para>
/// <para>
/// <b>Dispatch.</b> Code that only collects and dispatches events depends on
/// <see cref="IHasDomainEvents"/> instead, because dispatch needs no identity type.
/// </para>
/// </remarks>
public interface IAggregateRoot<TId> : IEntity<TId>, IHasDomainEvents where TId : notnull
{
}

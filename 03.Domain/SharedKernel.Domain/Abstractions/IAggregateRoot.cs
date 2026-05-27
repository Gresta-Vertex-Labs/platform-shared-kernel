namespace SharedKernel.Domain.Abstractions;

/// <summary>
/// Marks a type as a DDD aggregate root — the consistency boundary for a cluster of domain objects.
/// Extends <see cref="IEntity{TId}"/> and <see cref="IHasDomainEvents"/>, providing access to the
/// domain events raised during the current unit of work.
/// </summary>
/// <typeparam name="TId">The type of the aggregate's identity key. Must be non-null.</typeparam>
/// <remarks>
/// Infrastructure dispatch code must depend on <see cref="IHasDomainEvents"/> rather than
/// <see cref="IAggregateRoot{TId}"/> — aggregate identity is not required for event dispatch.
/// </remarks>
public interface IAggregateRoot<TId> : IEntity<TId>, IHasDomainEvents where TId : notnull
{
}

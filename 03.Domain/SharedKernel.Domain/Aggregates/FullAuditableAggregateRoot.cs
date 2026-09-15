using SharedKernel.Core.Exceptions;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Primitives.Clocks;

namespace SharedKernel.Domain.Aggregates;

/// <summary>
/// An aggregate root with audit metadata, soft deletion, and an optimistic concurrency token.
/// </summary>
/// <typeparam name="TId">The identity key type. Must be non-null.</typeparam>
/// <remarks>
/// Extends <see cref="AuditableSoftDeletableAggregateRoot{TId}"/> with <see cref="RowVersion"/>.
/// </remarks>
public abstract class FullAuditableAggregateRoot<TId> : AuditableSoftDeletableAggregateRoot<TId>, IHasConcurrency
    where TId : notnull
{
    /// <summary>Initializes the aggregate with its identity key and clock.</summary>
    /// <param name="id">The identity key.</param>
    /// <param name="clock">The clock that timestamps events and time-dependent state.</param>
    /// <exception cref="DomainException"><paramref name="clock"/> is <see langword="null"/>.</exception>
    protected FullAuditableAggregateRoot(TId id, IClock clock) : base(id, clock) { }

    /// <summary>Initializes the aggregate for ORM materialization. Do not call from domain code.</summary>
    protected FullAuditableAggregateRoot() { }

    /// <inheritdoc/>
    public byte[] RowVersion { get; protected set; } = [];
}

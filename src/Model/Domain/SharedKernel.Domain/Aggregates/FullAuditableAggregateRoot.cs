using SharedKernel.Core.Exceptions;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Primitives.Clocks;

namespace SharedKernel.Domain.Aggregates;

/// <summary>
/// An aggregate root with audit metadata, soft delete, and an optimistic concurrency token.
/// </summary>
/// <typeparam name="TId">The identity key type. Must be non-null.</typeparam>
/// <remarks>
/// Extends <see cref="AuditableSoftDeletableAggregateRoot{TId}"/> with <see cref="RowVersion"/>, which
/// the persistence layer owns and domain code never writes.
/// </remarks>
public abstract class FullAuditableAggregateRoot<TId> : AuditableSoftDeletableAggregateRoot<TId>, IHasConcurrency
    where TId : notnull
{
    /// <summary>Initializes a new aggregate with its identity key and clock.</summary>
    /// <param name="id">The identity key; <c>default(TId)</c> makes the aggregate transient.</param>
    /// <param name="clock">
    /// The clock that timestamps events and time-dependent state. Must not be <see langword="null"/>.
    /// </param>
    /// <exception cref="DomainException"><paramref name="clock"/> is <see langword="null"/>.</exception>
    protected FullAuditableAggregateRoot(TId id, IClock clock) : base(id, clock) { }

    /// <summary>
    /// Initializes a new aggregate without a clock, for ORM materialization only. Do not call from domain
    /// code.
    /// </summary>
    protected FullAuditableAggregateRoot() { }

    /// <inheritdoc/>
    public byte[] RowVersion { get; protected set; } = [];
}

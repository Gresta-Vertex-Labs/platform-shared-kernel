using SharedKernel.Domain.Abstractions;

namespace SharedKernel.Domain.Entities;

/// <summary>
/// A child entity inside an aggregate with audit metadata, soft delete, and an optimistic concurrency token.
/// </summary>
/// <typeparam name="TId">The identity key type. Must be non-null.</typeparam>
/// <remarks>
/// Extends <see cref="AuditableSoftDeletableEntity{TId}"/> with <see cref="RowVersion"/>, which the
/// persistence layer owns and domain code never writes.
/// </remarks>
public abstract class FullAuditableEntity<TId> : AuditableSoftDeletableEntity<TId>, IHasConcurrency
    where TId : notnull
{
    /// <summary>Initializes a new entity with its identity key.</summary>
    /// <param name="id">The identity key; <c>default(TId)</c> makes the entity transient.</param>
    protected FullAuditableEntity(TId id) : base(id) { }

    /// <summary>
    /// Initializes a new transient entity for ORM materialization only. Do not call from domain code.
    /// </summary>
    protected FullAuditableEntity() { }

    /// <inheritdoc/>
    public byte[] RowVersion { get; protected set; } = [];
}

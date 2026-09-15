using SharedKernel.Domain.Abstractions;

namespace SharedKernel.Domain.Entities;

/// <summary>
/// A child entity with audit metadata, soft deletion, and an optimistic concurrency token.
/// </summary>
/// <typeparam name="TId">The identity key type. Must be non-null.</typeparam>
/// <remarks>
/// Extends <see cref="AuditableSoftDeletableEntity{TId}"/> with <see cref="RowVersion"/>.
/// </remarks>
public abstract class FullAuditableEntity<TId> : AuditableSoftDeletableEntity<TId>, IHasConcurrency
    where TId : notnull
{
    /// <summary>Initializes the entity with its identity key.</summary>
    /// <param name="id">The identity key; <c>default(TId)</c> makes the entity transient.</param>
    protected FullAuditableEntity(TId id) : base(id) { }

    /// <summary>Initializes the entity for ORM materialization. Do not call from domain code.</summary>
    protected FullAuditableEntity() { }

    /// <inheritdoc/>
    public byte[] RowVersion { get; protected set; } = [];
}

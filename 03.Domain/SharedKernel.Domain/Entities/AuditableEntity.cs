using SharedKernel.Domain.Abstractions;

namespace SharedKernel.Domain.Entities;

/// <summary>A child entity that records who created and last modified it, and when.</summary>
/// <typeparam name="TId">The identity key type. Must be non-null.</typeparam>
/// <remarks>
/// The audit properties are written by the persistence layer when the entity is saved, never by domain
/// code. Entity bases mirror the aggregate bases without events or a clock:
/// <see cref="AuditableEntity{TId}"/>, <see cref="SoftDeletableEntity{TId}"/>,
/// <see cref="AuditableSoftDeletableEntity{TId}"/> and <see cref="FullAuditableEntity{TId}"/>.
/// </remarks>
public abstract class AuditableEntity<TId> : Entity<TId>, IHasAudit
    where TId : notnull
{
    /// <summary>Initializes the entity with its identity key.</summary>
    /// <param name="id">The identity key; <c>default(TId)</c> makes the entity transient.</param>
    protected AuditableEntity(TId id) : base(id) { }

    /// <summary>Initializes the entity for ORM materialization. Do not call from domain code.</summary>
    protected AuditableEntity() { }

    /// <inheritdoc/>
    public string CreatedBy { get; private set; } = string.Empty;

    /// <inheritdoc/>
    public DateTimeOffset CreatedOn { get; private set; }

    /// <inheritdoc/>
    public string? ModifiedBy { get; private set; }

    /// <inheritdoc/>
    public DateTimeOffset? ModifiedOn { get; private set; }
}

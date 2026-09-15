using SharedKernel.Core.Exceptions;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Domain.Internal;
using SharedKernel.Guards;

namespace SharedKernel.Domain.Entities;

/// <summary>A child entity with audit metadata that is deleted logically rather than removed.</summary>
/// <typeparam name="TId">The identity key type. Must be non-null.</typeparam>
/// <remarks>
/// Combines <see cref="AuditableEntity{TId}"/> and <see cref="SoftDeletableEntity{TId}"/>; see those types.
/// </remarks>
public abstract class AuditableSoftDeletableEntity<TId> : Entity<TId>, IHasAudit, ISoftDeletable
    where TId : notnull
{
    /// <summary>Initializes the entity with its identity key.</summary>
    /// <param name="id">The identity key; <c>default(TId)</c> makes the entity transient.</param>
    protected AuditableSoftDeletableEntity(TId id) : base(id) { }

    /// <summary>Initializes the entity for ORM materialization. Do not call from domain code.</summary>
    protected AuditableSoftDeletableEntity() { }

    /// <inheritdoc/>
    public string CreatedBy { get; private set; } = string.Empty;

    /// <inheritdoc/>
    public DateTimeOffset CreatedOn { get; private set; }

    /// <inheritdoc/>
    public string? ModifiedBy { get; private set; }

    /// <inheritdoc/>
    public DateTimeOffset? ModifiedOn { get; private set; }

    /// <inheritdoc/>
    public bool IsDeleted { get; private set; }

    /// <inheritdoc/>
    public DateTimeOffset? DeletedOn { get; private set; }

    /// <inheritdoc/>
    public string? DeletedBy { get; private set; }

    /// <summary>
    /// Marks the entity deleted by <paramref name="deletedBy"/> at <paramref name="deletedOn"/>. Does nothing
    /// when the entity is already deleted.
    /// </summary>
    /// <param name="deletedBy">The identifier of the actor performing the deletion.</param>
    /// <param name="deletedOn">The UTC time of the deletion, normally the owning aggregate's current time.</param>
    /// <exception cref="DomainException">
    /// <paramref name="deletedBy"/> is null, empty or whitespace, or <paramref name="deletedOn"/> is not UTC.
    /// </exception>
    /// <remarks>
    /// An entity has no clock, so the owning aggregate passes its own time. Expose a domain method on the
    /// entity that calls this, and call that method from the aggregate.
    /// </remarks>
    protected void MarkAsDeleted(string deletedBy, DateTimeOffset deletedOn)
    {
        Guard.Throw.NotUtc(deletedOn);
        if (!SoftDeletion.ShouldMarkDeleted(IsDeleted, deletedBy))
            return;

        DeletedOn = deletedOn;
        DeletedBy = deletedBy;
        IsDeleted = true;
    }
}

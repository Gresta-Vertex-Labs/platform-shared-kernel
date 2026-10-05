using SharedKernel.Core.Exceptions;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Domain.Internal;
using SharedKernel.Guards;

namespace SharedKernel.Domain.Entities;

/// <summary>
/// A child entity inside an aggregate that records who created and last modified it, and supports soft
/// delete.
/// </summary>
/// <typeparam name="TId">The identity key type. Must be non-null.</typeparam>
/// <remarks>
/// Combines <see cref="AuditableEntity{TId}"/> and <see cref="SoftDeletableEntity{TId}"/>; the audit and
/// soft-delete rules of those types apply unchanged.
/// </remarks>
public abstract class AuditableSoftDeletableEntity<TId> : Entity<TId>, IHasAudit, ISoftDeletable
    where TId : notnull
{
    /// <summary>Initializes a new entity with its identity key.</summary>
    /// <param name="id">The identity key; <c>default(TId)</c> makes the entity transient.</param>
    protected AuditableSoftDeletableEntity(TId id) : base(id) { }

    /// <summary>
    /// Initializes a new transient entity for ORM materialization only. Do not call from domain code.
    /// </summary>
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
    /// Soft-deletes the entity: records <paramref name="deletedBy"/> and <paramref name="deletedOn"/> and
    /// sets <see cref="IsDeleted"/>.
    /// </summary>
    /// <param name="deletedBy">The actor performing the deletion. Must not be null or whitespace.</param>
    /// <param name="deletedOn">
    /// The time of the deletion, normally the owning aggregate's current time. Must have a zero UTC offset.
    /// </param>
    /// <exception cref="DomainException">
    /// <paramref name="deletedOn"/> has a non-zero UTC offset, or <paramref name="deletedBy"/> is
    /// <see langword="null"/>, empty or whitespace. Both are checked even when the entity is already deleted.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <b>Time.</b> An entity has no clock, so the owning aggregate passes its own time. Expose a domain
    /// method on the entity that calls this, and call that method from the aggregate.
    /// </para>
    /// <para>
    /// <b>Idempotent.</b> When the entity is already deleted, the call changes nothing, so the original time
    /// and actor are kept. No domain event is raised; the owning aggregate raises one if needed.
    /// </para>
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

using SharedKernel.Core.Exceptions;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Domain.Internal;
using SharedKernel.Guards;

namespace SharedKernel.Domain.Entities;

/// <summary>
/// A child entity inside an aggregate that supports soft delete: it is marked deleted and kept, rather
/// than removed.
/// </summary>
/// <typeparam name="TId">The identity key type. Must be non-null.</typeparam>
/// <remarks>
/// <b>Usage.</b> The owning aggregate soft-deletes the entity through a domain method that passes the
/// aggregate's own time to <see cref="MarkAsDeleted"/>, and raises any deletion event itself.
/// </remarks>
/// <example>
/// <code>
/// public sealed class OrderLine : SoftDeletableEntity&lt;OrderLineId&gt;
/// {
///     internal void Remove(string removedBy, DateTimeOffset at) =&gt; MarkAsDeleted(removedBy, at);
/// }
///
/// // In the Order aggregate:
/// public void RemoveLine(OrderLineId lineId, string removedBy) =&gt;
///     _lines.Single(l =&gt; l.Id == lineId).Remove(removedBy, Now);
/// </code>
/// </example>
public abstract class SoftDeletableEntity<TId> : Entity<TId>, ISoftDeletable
    where TId : notnull
{
    /// <summary>Initializes a new entity with its identity key.</summary>
    /// <param name="id">The identity key; <c>default(TId)</c> makes the entity transient.</param>
    protected SoftDeletableEntity(TId id) : base(id) { }

    /// <summary>
    /// Initializes a new transient entity for ORM materialization only. Do not call from domain code.
    /// </summary>
    protected SoftDeletableEntity() { }

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

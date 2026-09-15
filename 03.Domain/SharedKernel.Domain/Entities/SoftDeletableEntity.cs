using SharedKernel.Core.Exceptions;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Domain.Internal;
using SharedKernel.Guards;

namespace SharedKernel.Domain.Entities;

/// <summary>A child entity that is deleted logically: marked deleted and kept, rather than removed.</summary>
/// <typeparam name="TId">The identity key type. Must be non-null.</typeparam>
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
    /// <summary>Initializes the entity with its identity key.</summary>
    /// <param name="id">The identity key; <c>default(TId)</c> makes the entity transient.</param>
    protected SoftDeletableEntity(TId id) : base(id) { }

    /// <summary>Initializes the entity for ORM materialization. Do not call from domain code.</summary>
    protected SoftDeletableEntity() { }

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

using SharedKernel.Core.Exceptions;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Domain.Internal;
using SharedKernel.Primitives.Clocks;

namespace SharedKernel.Domain.Aggregates;

/// <summary>
/// An aggregate root that records who created and last modified it, and supports soft delete.
/// </summary>
/// <typeparam name="TId">The identity key type. Must be non-null.</typeparam>
/// <remarks>
/// Combines <see cref="AuditableAggregateRoot{TId}"/> and <see cref="SoftDeletableAggregateRoot{TId}"/>;
/// the audit and soft-delete rules of those types apply unchanged.
/// </remarks>
public abstract class AuditableSoftDeletableAggregateRoot<TId> : AggregateRoot<TId>, IHasAudit, ISoftDeletable
    where TId : notnull
{
    /// <summary>Initializes a new aggregate with its identity key and clock.</summary>
    /// <param name="id">The identity key; <c>default(TId)</c> makes the aggregate transient.</param>
    /// <param name="clock">
    /// The clock that timestamps events and time-dependent state. Must not be <see langword="null"/>.
    /// </param>
    /// <exception cref="DomainException"><paramref name="clock"/> is <see langword="null"/>.</exception>
    protected AuditableSoftDeletableAggregateRoot(TId id, IClock clock) : base(id, clock) { }

    /// <summary>
    /// Initializes a new aggregate without a clock, for ORM materialization only. Do not call from domain
    /// code.
    /// </summary>
    protected AuditableSoftDeletableAggregateRoot() { }

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
    /// Soft-deletes the aggregate: records <paramref name="deletedBy"/> and the clock's current time, sets
    /// <see cref="IsDeleted"/>, then calls <see cref="OnDelete"/>.
    /// </summary>
    /// <param name="deletedBy">The actor performing the deletion. Must not be null or whitespace.</param>
    /// <exception cref="DomainException">
    /// <paramref name="deletedBy"/> is <see langword="null"/>, empty or whitespace, even when the aggregate
    /// is already deleted.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The aggregate is not yet deleted and no clock is attached.
    /// </exception>
    /// <remarks>
    /// <b>Idempotent.</b> When the aggregate is already deleted, the call changes nothing, does not read the
    /// clock and does not call <see cref="OnDelete"/> again, so the original time and actor are kept.
    /// </remarks>
    protected void MarkAsDeleted(string deletedBy)
    {
        if (!SoftDeletion.ShouldMarkDeleted(IsDeleted, deletedBy))
            return;

        DeletedOn = Now;
        DeletedBy = deletedBy;
        IsDeleted = true;
        OnDelete();
    }

    /// <summary>
    /// Called once, right after <see cref="MarkAsDeleted"/> first marks the aggregate deleted; override it
    /// to raise the deletion event.
    /// </summary>
    /// <remarks>
    /// The deleted state is already set when it runs and stays set if it throws. It is not called when the
    /// persistence layer soft-deletes a removed aggregate.
    /// </remarks>
    protected virtual void OnDelete()
    {
    }

    /// <summary>
    /// Reverses a soft delete: clears <see cref="IsDeleted"/>, <see cref="DeletedOn"/> and
    /// <see cref="DeletedBy"/>, then calls <see cref="OnRestore"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Usage.</b> Call it from a domain method such as <c>Reopen()</c>, and raise the restore event from
    /// <see cref="OnRestore"/>. Load the aggregate with a specification that includes deleted entities, then save
    /// through the unit of work; the change goes through the normal audit and domain-event pipeline.
    /// </para>
    /// <para>
    /// <b>Idempotent.</b> Restoring an aggregate that is not deleted changes nothing and does not call
    /// <see cref="OnRestore"/>.
    /// </para>
    /// </remarks>
    protected void Restore()
    {
        if (!IsDeleted)
            return;

        IsDeleted = false;
        DeletedOn = null;
        DeletedBy = null;
        OnRestore();
    }

    /// <summary>
    /// Called once, right after <see cref="Restore"/> clears the deleted state; override it to raise the
    /// restore event. Does nothing by default.
    /// </summary>
    protected virtual void OnRestore()
    {
    }
}

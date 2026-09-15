using SharedKernel.Core.Exceptions;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Domain.Internal;
using SharedKernel.Primitives.Clocks;

namespace SharedKernel.Domain.Aggregates;

/// <summary>An aggregate root with audit metadata that is deleted logically rather than removed.</summary>
/// <typeparam name="TId">The identity key type. Must be non-null.</typeparam>
/// <remarks>
/// Combines <see cref="AuditableAggregateRoot{TId}"/> and <see cref="SoftDeletableAggregateRoot{TId}"/>;
/// see those types for the audit and soft-delete rules.
/// </remarks>
public abstract class AuditableSoftDeletableAggregateRoot<TId> : AggregateRoot<TId>, IHasAudit, ISoftDeletable
    where TId : notnull
{
    /// <summary>Initializes the aggregate with its identity key and clock.</summary>
    /// <param name="id">The identity key.</param>
    /// <param name="clock">The clock that timestamps events and time-dependent state.</param>
    /// <exception cref="DomainException"><paramref name="clock"/> is <see langword="null"/>.</exception>
    protected AuditableSoftDeletableAggregateRoot(TId id, IClock clock) : base(id, clock) { }

    /// <summary>Initializes the aggregate for ORM materialization. Do not call from domain code.</summary>
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
    /// Marks the aggregate deleted by <paramref name="deletedBy"/> at the clock's current time, then
    /// calls <see cref="OnDelete"/>. Does nothing when the aggregate is already deleted.
    /// </summary>
    /// <param name="deletedBy">The identifier of the actor performing the deletion.</param>
    /// <exception cref="DomainException"><paramref name="deletedBy"/> is null, empty or whitespace.</exception>
    /// <exception cref="InvalidOperationException">No clock is attached.</exception>
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
    /// Called once, right after the aggregate is first marked deleted. Raise the deletion event here.
    /// </summary>
    protected abstract void OnDelete();
}

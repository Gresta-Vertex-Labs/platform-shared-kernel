using SharedKernel.Domain.Abstractions;
using SharedKernel.Primitives.Clocks;

namespace SharedKernel.Domain.Aggregates;

/// <summary>
/// Abstract aggregate root that implements <see cref="ISoftDeletable"/>, enabling logical deletion
/// without physical record removal.
/// </summary>
/// <typeparam name="TId">The type of the aggregate's identity key. Must be non-null.</typeparam>
/// <remarks>
/// <para>
/// Subclasses override <see cref="OnDelete"/> to raise the appropriate domain event when
/// the aggregate is soft-deleted.
/// </para>
/// <para>
/// Soft-delete fields (<see cref="IsDeleted"/>, <see cref="DeletedOn"/>, <see cref="DeletedBy"/>)
/// have <c>private set</c> and are only written by <see cref="MarkAsDeleted"/>.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public sealed class Customer : SoftDeletableAggregateRoot&lt;CustomerId&gt;
/// {
///     public string Email { get; private set; }
///
///     public Customer(CustomerId id, string email, IClock clock) : base(id, clock)
///     {
///         Email = email;
///     }
///
///     protected Customer() { } // ORM path
///
///     protected override void OnDelete()
///     {
///         RaiseDomainEvent(ts =&gt; new CustomerDeletedEvent(Id.Value) { OccurredOn = ts });
///     }
///
///     public void Delete(string deletedBy) =&gt; MarkAsDeleted(deletedBy);
/// }
/// </code>
/// </example>
public abstract class SoftDeletableAggregateRoot<TId> : AggregateRoot<TId>, ISoftDeletable
    where TId : notnull
{
    /// <summary>
    /// Initialises a new soft-deletable aggregate root with the specified identity key and clock.
    /// </summary>
    protected SoftDeletableAggregateRoot(TId id, IClock clock) : base(id, clock) { }

    /// <summary>
    /// Protected parameterless constructor for ORM materialisation paths.
    /// </summary>
    protected SoftDeletableAggregateRoot() : base() { }

    /// <inheritdoc/>
    public bool IsDeleted { get; private set; }

    /// <inheritdoc/>
    public DateTimeOffset? DeletedOn { get; private set; }

    /// <inheritdoc/>
    public string? DeletedBy { get; private set; }

    /// <summary>
    /// Called by <see cref="MarkAsDeleted"/> after soft-delete fields are set.
    /// Override to raise the domain event that signals deletion.
    /// </summary>
    protected abstract void OnDelete();

    /// <summary>
    /// Sets the soft-delete fields and calls <see cref="OnDelete"/> so subclasses can raise
    /// the appropriate domain event.
    /// </summary>
    /// <param name="deletedBy">The identifier of the actor performing the deletion.</param>
    protected void MarkAsDeleted(string deletedBy)
    {
        IsDeleted = true;
        DeletedOn = Now;
        DeletedBy = deletedBy;
        OnDelete();
    }
}

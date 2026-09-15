using SharedKernel.Core.Exceptions;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Domain.Internal;
using SharedKernel.Primitives.Clocks;

namespace SharedKernel.Domain.Aggregates;

/// <summary>
/// An aggregate root that is deleted logically: it is marked deleted and kept, rather than removed.
/// </summary>
/// <typeparam name="TId">The identity key type. Must be non-null.</typeparam>
/// <remarks>
/// <para>
/// Delete through a domain method that calls <see cref="MarkAsDeleted"/>, and raise the deletion event
/// from <see cref="OnDelete"/>. Removing the aggregate through a repository also soft-deletes it, via
/// the persistence layer's interceptor, but raises no domain event, so prefer the domain method
/// whenever other parts of the system must react to the deletion.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public sealed class Customer : SoftDeletableAggregateRoot&lt;CustomerId&gt;
/// {
///     public void Close(string closedBy) =&gt; MarkAsDeleted(closedBy);
///
///     protected override void OnDelete() =&gt;
///         RaiseDomainEvent(at =&gt; new CustomerClosed(Id.Value) { OccurredOn = at });
/// }
/// </code>
/// </example>
public abstract class SoftDeletableAggregateRoot<TId> : AggregateRoot<TId>, ISoftDeletable
    where TId : notnull
{
    /// <summary>Initializes the aggregate with its identity key and clock.</summary>
    /// <param name="id">The identity key.</param>
    /// <param name="clock">The clock that timestamps events and time-dependent state.</param>
    /// <exception cref="DomainException"><paramref name="clock"/> is <see langword="null"/>.</exception>
    protected SoftDeletableAggregateRoot(TId id, IClock clock) : base(id, clock) { }

    /// <summary>Initializes the aggregate for ORM materialization. Do not call from domain code.</summary>
    protected SoftDeletableAggregateRoot() { }

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

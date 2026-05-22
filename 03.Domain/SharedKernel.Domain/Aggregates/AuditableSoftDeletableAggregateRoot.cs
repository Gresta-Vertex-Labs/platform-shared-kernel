using SharedKernel.Domain.Abstractions;
using SharedKernel.Primitives.Clocks;

namespace SharedKernel.Domain.Aggregates;

/// <summary>
/// Abstract aggregate root combining both <see cref="IHasAudit"/> and <see cref="ISoftDeletable"/>
/// capabilities.
/// </summary>
/// <typeparam name="TId">The type of the aggregate's identity key. Must be non-null.</typeparam>
/// <remarks>
/// All audit and soft-delete fields have <c>private set</c> — populated exclusively by
/// persistence-layer conventions, except <see cref="IsDeleted"/>, <see cref="DeletedOn"/>,
/// and <see cref="DeletedBy"/> which are written by <see cref="MarkAsDeleted"/>.
/// </remarks>
/// <example>
/// <code>
/// public sealed class Invoice : AuditableSoftDeletableAggregateRoot&lt;InvoiceId&gt;
/// {
///     public decimal Total { get; private set; }
///
///     public Invoice(InvoiceId id, decimal total, IClock clock) : base(id, clock)
///     {
///         Total = total;
///     }
///
///     protected Invoice() { } // ORM path
///
///     protected override void OnDelete()
///     {
///         RaiseDomainEvent(ts =&gt; new InvoiceVoidedEvent(Id.Value) { OccurredOn = ts });
///     }
/// }
/// </code>
/// </example>
public abstract class AuditableSoftDeletableAggregateRoot<TId> : AggregateRoot<TId>, IHasAudit, ISoftDeletable
    where TId : notnull
{
    /// <summary>
    /// Initialises a new aggregate root with the specified identity key and clock.
    /// </summary>
    protected AuditableSoftDeletableAggregateRoot(TId id, IClock clock) : base(id, clock) { }

    /// <summary>
    /// Protected parameterless constructor for ORM materialisation paths.
    /// </summary>
    protected AuditableSoftDeletableAggregateRoot() : base() { }

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

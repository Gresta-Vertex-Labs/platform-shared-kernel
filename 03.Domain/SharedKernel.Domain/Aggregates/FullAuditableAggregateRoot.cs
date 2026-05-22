using SharedKernel.Domain.Abstractions;
using SharedKernel.Primitives.Clocks;

namespace SharedKernel.Domain.Aggregates;

/// <summary>
/// Abstract aggregate root implementing <see cref="IHasAudit"/>, <see cref="ISoftDeletable"/>,
/// and <see cref="IHasConcurrency"/> — providing the full auditable stack.
/// </summary>
/// <typeparam name="TId">The type of the aggregate's identity key. Must be non-null.</typeparam>
/// <remarks>
/// <para>
/// All audit and soft-delete fields have <c>private set</c> — populated exclusively by
/// persistence-layer conventions. <see cref="RowVersion"/> has <c>protected set</c> so the
/// persistence layer can write it after fetch without going through EF Core shadow properties.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public sealed class Contract : FullAuditableAggregateRoot&lt;ContractId&gt;
/// {
///     public string Title { get; private set; }
///
///     public Contract(ContractId id, string title, IClock clock) : base(id, clock)
///     {
///         Title = title;
///     }
///
///     protected Contract() { } // ORM path
///
///     protected override void OnDelete()
///     {
///         RaiseDomainEvent(ts =&gt; new ContractTerminatedEvent(Id.Value) { OccurredOn = ts });
///     }
/// }
/// </code>
/// </example>
public abstract class FullAuditableAggregateRoot<TId> : AggregateRoot<TId>, IHasAudit, ISoftDeletable, IHasConcurrency
    where TId : notnull
{
    /// <summary>
    /// Initialises a new aggregate root with the specified identity key and clock.
    /// </summary>
    protected FullAuditableAggregateRoot(TId id, IClock clock) : base(id, clock) { }

    /// <summary>
    /// Protected parameterless constructor for ORM materialisation paths.
    /// </summary>
    protected FullAuditableAggregateRoot() : base() { }

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
    /// Gets or sets the opaque concurrency token.
    /// <c>protected set</c> allows the persistence layer to populate this after a fetch.
    /// </summary>
    public byte[] RowVersion { get; protected set; } = [];

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

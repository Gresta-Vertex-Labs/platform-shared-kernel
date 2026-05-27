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
/// Extends <see cref="AuditableSoftDeletableAggregateRoot{TId}"/> — all audit and soft-delete fields,
/// <c>MarkAsDeleted</c>, and <c>OnDelete</c> are inherited from the hierarchy.
/// This class contributes only the <see cref="IHasConcurrency"/> explicit interface declaration
/// and <see cref="RowVersion"/> with <c>protected set</c> so the persistence layer can populate
/// it after a fetch.
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
public abstract class FullAuditableAggregateRoot<TId> : AuditableSoftDeletableAggregateRoot<TId>, IHasAudit, ISoftDeletable, IHasConcurrency
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

    /// <summary>
    /// Gets or sets the opaque concurrency token.
    /// <c>protected set</c> allows the persistence layer to populate this after a fetch.
    /// </summary>
    public byte[] RowVersion { get; protected set; } = [];
}

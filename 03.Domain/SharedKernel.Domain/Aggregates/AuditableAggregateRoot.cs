using SharedKernel.Core.Exceptions;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Primitives.Clocks;

namespace SharedKernel.Domain.Aggregates;

/// <summary>An aggregate root that records who created and last modified it, and when.</summary>
/// <typeparam name="TId">The identity key type. Must be non-null.</typeparam>
/// <remarks>
/// <para>
/// <b>Persistence.</b> The persistence layer writes the audit properties when the aggregate is saved;
/// domain code never sets them, which is why their setters are private.
/// </para>
/// <para><b>Choosing a base class.</b></para>
/// <list type="table">
/// <listheader><term>Base class</term><description>Adds</description></listheader>
/// <item>
/// <term><see cref="AggregateRoot{TId}"/></term>
/// <description>Events, business rules, clock</description>
/// </item>
/// <item>
/// <term><see cref="AuditableAggregateRoot{TId}"/></term>
/// <description>Audit</description>
/// </item>
/// <item>
/// <term><see cref="SoftDeletableAggregateRoot{TId}"/></term>
/// <description>Soft delete</description>
/// </item>
/// <item>
/// <term><see cref="AuditableSoftDeletableAggregateRoot{TId}"/></term>
/// <description>Audit, soft delete</description>
/// </item>
/// <item>
/// <term><see cref="FullAuditableAggregateRoot{TId}"/></term>
/// <description>Audit, soft delete, concurrency token</description>
/// </item>
/// </list>
/// <para>
/// Each has a <c>Tenanted</c> counterpart that adds <see cref="IHasTenant"/>. For a combination not
/// listed, extend <see cref="AggregateRoot{TId}"/> and implement the interfaces directly: the
/// persistence layer reads the interfaces, never the base classes.
/// </para>
/// </remarks>
public abstract class AuditableAggregateRoot<TId> : AggregateRoot<TId>, IHasAudit
    where TId : notnull
{
    /// <summary>Initializes a new aggregate with its identity key and clock.</summary>
    /// <param name="id">The identity key; <c>default(TId)</c> makes the aggregate transient.</param>
    /// <param name="clock">
    /// The clock that timestamps events and time-dependent state. Must not be <see langword="null"/>.
    /// </param>
    /// <exception cref="DomainException"><paramref name="clock"/> is <see langword="null"/>.</exception>
    protected AuditableAggregateRoot(TId id, IClock clock) : base(id, clock) { }

    /// <summary>
    /// Initializes a new aggregate without a clock, for ORM materialization only. Do not call from domain
    /// code.
    /// </summary>
    protected AuditableAggregateRoot() { }

    /// <inheritdoc/>
    public string CreatedBy { get; private set; } = string.Empty;

    /// <inheritdoc/>
    public DateTimeOffset CreatedOn { get; private set; }

    /// <inheritdoc/>
    public string? ModifiedBy { get; private set; }

    /// <inheritdoc/>
    public DateTimeOffset? ModifiedOn { get; private set; }
}

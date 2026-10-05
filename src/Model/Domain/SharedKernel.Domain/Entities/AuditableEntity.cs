using SharedKernel.Domain.Abstractions;

namespace SharedKernel.Domain.Entities;

/// <summary>
/// A child entity inside an aggregate that records who created and last modified it, and when.
/// </summary>
/// <typeparam name="TId">The identity key type. Must be non-null.</typeparam>
/// <remarks>
/// <para>
/// <b>Persistence.</b> The persistence layer writes the audit properties when the entity is saved;
/// domain code never sets them.
/// </para>
/// <para>
/// <b>Choosing a base class.</b> The entity bases mirror the aggregate bases without events or a clock:
/// <see cref="AuditableEntity{TId}"/>, <see cref="SoftDeletableEntity{TId}"/>,
/// <see cref="AuditableSoftDeletableEntity{TId}"/> and <see cref="FullAuditableEntity{TId}"/>.
/// </para>
/// </remarks>
public abstract class AuditableEntity<TId> : Entity<TId>, IHasAudit
    where TId : notnull
{
    /// <summary>Initializes a new entity with its identity key.</summary>
    /// <param name="id">The identity key; <c>default(TId)</c> makes the entity transient.</param>
    protected AuditableEntity(TId id) : base(id) { }

    /// <summary>
    /// Initializes a new transient entity for ORM materialization only. Do not call from domain code.
    /// </summary>
    protected AuditableEntity() { }

    /// <inheritdoc/>
    public string CreatedBy { get; private set; } = string.Empty;

    /// <inheritdoc/>
    public DateTimeOffset CreatedOn { get; private set; }

    /// <inheritdoc/>
    public string? ModifiedBy { get; private set; }

    /// <inheritdoc/>
    public DateTimeOffset? ModifiedOn { get; private set; }
}

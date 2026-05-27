using SharedKernel.Domain.Abstractions;
using SharedKernel.Primitives.Clocks;

namespace SharedKernel.Domain.Aggregates;

/// <summary>
/// Abstract aggregate root that adds tenant isolation to the full auditable base.
/// Extends <see cref="FullAuditableAggregateRoot{TId}"/> and implements <see cref="IHasTenant"/>.
/// Inherits audit, soft-delete, <c>MarkAsDeleted</c>, <c>OnDelete</c>, and <see cref="FullAuditableAggregateRoot{TId}.RowVersion"/>
/// from the hierarchy.
/// </summary>
/// <typeparam name="TId">The type of the aggregate's identity key. Must be non-null.</typeparam>
/// <remarks>
/// <para>
/// <see cref="TenantId"/> is set exclusively at construction time and must never change.
/// Tenant reassignment is a domain violation. The application layer passes the tenant identifier
/// as a <c>Guid</c> primitive — the domain layer must never reference <c>ITenantProvider</c>
/// from <c>12.Security</c> directly.
/// </para>
/// <para>
/// The ORM-path parameterless constructor leaves <see cref="TenantId"/> as <see cref="Guid.Empty"/>.
/// </para>
/// </remarks>
public abstract class TenantedFullAuditableAggregateRoot<TId> : FullAuditableAggregateRoot<TId>, IHasTenant
    where TId : notnull
{
    /// <summary>
    /// Initialises a new tenanted full-auditable aggregate root with the specified identity key,
    /// tenant, and clock.
    /// </summary>
    /// <param name="id">The aggregate's identity key.</param>
    /// <param name="tenantId">The tenant identifier. Supplied by the application layer.</param>
    /// <param name="clock">The clock used to timestamp domain events raised by this aggregate.</param>
    protected TenantedFullAuditableAggregateRoot(TId id, Guid tenantId, IClock clock) : base(id, clock)
    {
        TenantId = tenantId;
    }

    /// <summary>
    /// Protected parameterless constructor for ORM materialisation paths.
    /// <see cref="TenantId"/> will be <see cref="Guid.Empty"/> until populated by the ORM.
    /// </summary>
    protected TenantedFullAuditableAggregateRoot() : base() { }

    /// <inheritdoc/>
    public Guid TenantId { get; private set; }
}

using SharedKernel.Core.Exceptions;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Guards;
using SharedKernel.Primitives.Clocks;

namespace SharedKernel.Domain.Aggregates;

/// <summary>
/// An aggregate root that records who created and last modified it, and belongs to exactly one tenant.
/// </summary>
/// <typeparam name="TId">The identity key type. Must be non-null.</typeparam>
/// <remarks>
/// <para>
/// <b>Tenant.</b> Extends <see cref="AuditableAggregateRoot{TId}"/> with <see cref="TenantId"/>, fixed at
/// construction and never changed. The application layer supplies it from the resolved tenant context;
/// the domain never resolves tenants itself.
/// </para>
/// <para>
/// <b>Persistence.</b> The ORM-materialization constructor leaves <see cref="TenantId"/> as
/// <see cref="Guid.Empty"/> until the ORM populates it.
/// </para>
/// </remarks>
public abstract class TenantedAuditableAggregateRoot<TId> : AuditableAggregateRoot<TId>, IHasTenant
    where TId : notnull
{
    /// <summary>Initializes a new aggregate with its identity key, owning tenant and clock.</summary>
    /// <param name="id">The identity key; <c>default(TId)</c> makes the aggregate transient.</param>
    /// <param name="tenantId">The identifier of the owning tenant. Must not be <see cref="Guid.Empty"/>.</param>
    /// <param name="clock">
    /// The clock that timestamps events and time-dependent state. Must not be <see langword="null"/>.
    /// </param>
    /// <exception cref="DomainException">
    /// <paramref name="clock"/> is <see langword="null"/>, or <paramref name="tenantId"/> is
    /// <see cref="Guid.Empty"/>.
    /// </exception>
    protected TenantedAuditableAggregateRoot(TId id, Guid tenantId, IClock clock) : base(id, clock)
    {
        Guard.Throw.InvalidGuid(tenantId);
        TenantId = tenantId;
    }

    /// <summary>
    /// Initializes a new aggregate without a clock or tenant, for ORM materialization only. Do not call
    /// from domain code.
    /// </summary>
    protected TenantedAuditableAggregateRoot() { }

    /// <inheritdoc/>
    public Guid TenantId { get; private set; }
}

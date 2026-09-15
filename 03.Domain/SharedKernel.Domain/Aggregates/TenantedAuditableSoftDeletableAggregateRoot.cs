using SharedKernel.Core.Exceptions;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Guards;
using SharedKernel.Primitives.Clocks;

namespace SharedKernel.Domain.Aggregates;

/// <summary>A soft-deletable aggregate root with audit metadata that belongs to exactly one tenant.</summary>
/// <typeparam name="TId">The identity key type. Must be non-null.</typeparam>
/// <remarks>
/// <para>
/// Extends <see cref="AuditableSoftDeletableAggregateRoot{TId}"/> with <see cref="TenantId"/>. The tenant is fixed at construction and
/// never changes; the application layer supplies it from the resolved tenant context, because the domain
/// never resolves tenants itself.
/// </para>
/// <para>
/// The ORM-materialization constructor leaves <see cref="TenantId"/> empty until the ORM populates it.
/// </para>
/// </remarks>
public abstract class TenantedAuditableSoftDeletableAggregateRoot<TId> : AuditableSoftDeletableAggregateRoot<TId>, IHasTenant
    where TId : notnull
{
    /// <summary>Initializes the aggregate with its identity key, owning tenant and clock.</summary>
    /// <param name="id">The identity key.</param>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="clock">The clock that timestamps events and time-dependent state.</param>
    /// <exception cref="DomainException">
    /// <paramref name="tenantId"/> is <see cref="Guid.Empty"/>, or <paramref name="clock"/> is <see langword="null"/>.
    /// </exception>
    protected TenantedAuditableSoftDeletableAggregateRoot(TId id, Guid tenantId, IClock clock) : base(id, clock)
    {
        Guard.Throw.InvalidGuid(tenantId);
        TenantId = tenantId;
    }

    /// <summary>Initializes the aggregate for ORM materialization. Do not call from domain code.</summary>
    protected TenantedAuditableSoftDeletableAggregateRoot() { }

    /// <inheritdoc/>
    public Guid TenantId { get; private set; }
}

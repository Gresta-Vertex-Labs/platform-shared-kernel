using SharedKernel.Execution.Tenancy;
using SharedKernel.Core.Exceptions;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Guards;
using SharedKernel.Primitives.Clocks;

namespace SharedKernel.Domain.Aggregates;

/// <summary>
/// An aggregate root with audit metadata and soft delete that belongs to exactly one tenant.
/// </summary>
/// <typeparam name="TId">The identity key type. Must be non-null.</typeparam>
/// <remarks>
/// <para>
/// <b>Tenant.</b> Extends <see cref="AuditableSoftDeletableAggregateRoot{TId}"/> with
/// <see cref="TenantId"/>, fixed at construction and never changed. The application layer supplies it
/// from the resolved tenant context; the domain never resolves tenants itself.
/// </para>
/// <para>
/// <b>Persistence.</b> The ORM-materialization constructor leaves <see cref="TenantId"/> as
/// <c>default(TenantId)</c> until the ORM populates it.
/// </para>
/// </remarks>
public abstract class TenantedAuditableSoftDeletableAggregateRoot<TId> : AuditableSoftDeletableAggregateRoot<TId>, IHasTenant
    where TId : notnull
{
    /// <summary>Initializes a new aggregate with its identity key, owning tenant and clock.</summary>
    /// <param name="id">The identity key; <c>default(TId)</c> makes the aggregate transient.</param>
    /// <param name="tenantId">The identifier of the owning tenant. Must not be <c>default(TenantId)</c>.</param>
    /// <param name="clock">
    /// The clock that timestamps events and time-dependent state. Must not be <see langword="null"/>.
    /// </param>
    /// <exception cref="DomainException">
    /// <paramref name="clock"/> is <see langword="null"/>, or <paramref name="tenantId"/> is
    /// <c>default(TenantId)</c>.
    /// </exception>
    protected TenantedAuditableSoftDeletableAggregateRoot(TId id, TenantId tenantId, IClock clock) : base(id, clock)
    {
        Guard.Throw.InvalidGuid(tenantId.Value, nameof(tenantId));
        TenantId = tenantId;
    }

    /// <summary>
    /// Initializes a new aggregate without a clock or tenant, for ORM materialization only. Do not call
    /// from domain code.
    /// </summary>
    protected TenantedAuditableSoftDeletableAggregateRoot() { }

    /// <inheritdoc/>
    public TenantId TenantId { get; private set; }
}

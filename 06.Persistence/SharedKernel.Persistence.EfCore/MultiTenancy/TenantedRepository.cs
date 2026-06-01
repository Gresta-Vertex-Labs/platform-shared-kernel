using Microsoft.EntityFrameworkCore;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Persistence.EfCore.Repositories;

namespace SharedKernel.Persistence.EfCore.MultiTenancy;

/// <summary>
/// Abstract repository base for multi-tenant aggregates.
/// Extends <see cref="EfRepository{TAggregate,TId}"/> with an explicit cross-tenant lookup
/// that bypasses the global tenant query filter.
/// </summary>
/// <typeparam name="TAggregate">
/// The aggregate root type. Must implement <see cref="IAggregateRoot{TId}"/> and
/// <see cref="IHasTenant"/>.
/// </typeparam>
/// <typeparam name="TId">The aggregate's identity type. Must be non-null.</typeparam>
/// <remarks>
/// <para>
/// Standard <c>GetByIdAsync</c> routes through the global tenant filter automatically —
/// the result is always scoped to the current tenant without any extra code.
/// </para>
/// <para>
/// Use <see cref="GetByIdForTenantAsync"/> only from admin or migration code that needs to
/// operate on data outside the current tenant scope.
/// </para>
/// </remarks>
public abstract class TenantedRepository<TAggregate, TId> : EfRepository<TAggregate, TId>
    where TAggregate : class, IAggregateRoot<TId>, IHasTenant
    where TId : notnull
{
    /// <summary>
    /// Initialises a new <see cref="TenantedRepository{TAggregate, TId}"/>.
    /// </summary>
    /// <param name="dbContext">The scoped tenanted DB context.</param>
    protected TenantedRepository(TenantedDbContext dbContext)
        : base(dbContext)
    {
    }

    /// <summary>
    /// Retrieves an aggregate by its identity under an explicit <paramref name="tenantId"/>,
    /// bypassing the global tenant query filter.
    /// </summary>
    /// <param name="id">The aggregate's unique identifier.</param>
    /// <param name="tenantId">The tenant scope to search within.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// The aggregate if found within the specified tenant scope; otherwise <see langword="null"/>.
    /// </returns>
    /// <remarks>
    /// This method is intended for admin and migration paths only. Standard application code
    /// should always use <see cref="EfRepository{TAggregate,TId}.GetByIdAsync"/> which is
    /// filtered to the current tenant automatically.
    /// </remarks>
    public virtual async Task<TAggregate?> GetByIdForTenantAsync(
        TId id,
        Guid tenantId,
        CancellationToken ct = default)
    {
        return await DbContext.Set<TAggregate>()
            .IgnoreQueryFilters()
            .Where(e => e.TenantId == tenantId)
            .FirstOrDefaultAsync(e => EF.Property<TId>(e, "Id")!.Equals(id), ct);
    }
}

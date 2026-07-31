using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Persistence.EfCore.Repositories;

namespace SharedKernel.Persistence.EfCore.MultiTenancy;

/// <summary>
/// Abstract repository base for multi-tenant aggregates.
/// Extends <see cref="EfRepository{TAggregate,TId}"/> with explicit cross-tenant lookups
/// that bypass the global tenant query filter.
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
/// Use <see cref="GetByIdForTenantAsync"/> for admin or migration code that must operate within
/// an explicit tenant scope but must still exclude soft-deleted records.
/// </para>
/// <para>
/// Use <see cref="GetByIdForTenantIncludingDeletedAsync"/> only for audit, data-export, or
/// recovery operations where both filters must be bypassed.
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
    /// bypassing the global tenant filter while preserving the soft-delete filter for
    /// <see cref="ISoftDeletable"/> entities.
    /// </summary>
    /// <param name="id">The aggregate's unique identifier.</param>
    /// <param name="tenantId">The tenant scope to search within.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// The aggregate if found within the specified tenant scope and not soft-deleted;
    /// otherwise <see langword="null"/>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <strong>Filter semantics:</strong>
    /// <list type="bullet">
    ///   <item><description>
    ///     The <strong>tenant filter is bypassed</strong> — records are searched within the
    ///     explicitly provided <paramref name="tenantId"/>.
    ///   </description></item>
    ///   <item><description>
    ///     The <strong>soft-delete filter is preserved</strong> for <see cref="ISoftDeletable"/>
    ///     entities — soft-deleted records are excluded. EF Core cannot selectively bypass one
    ///     global query filter; the workaround is to call <c>IgnoreQueryFilters()</c> and
    ///     re-apply the soft-delete condition manually.
    ///   </description></item>
    ///   <item><description>
    ///     For non-<see cref="ISoftDeletable"/> aggregates, this method is equivalent to
    ///     <see cref="GetByIdForTenantIncludingDeletedAsync"/>.
    ///   </description></item>
    /// </list>
    /// </para>
    /// <para>
    /// This method is intended for admin and migration paths only. Standard application code
    /// should always use <see cref="EfRepository{TAggregate,TId}.GetByIdAsync"/> which is
    /// filtered to the current tenant automatically.
    /// </para>
    /// <para>
    /// Do not use this method for audit or recovery — use
    /// <see cref="GetByIdForTenantIncludingDeletedAsync"/> instead.
    /// </para>
    /// </remarks>
    public virtual async Task<TAggregate?> GetByIdForTenantAsync(
        TId id,
        Guid tenantId,
        CancellationToken ct = default)
    {
        var query = DbContext.Set<TAggregate>()
            .IgnoreQueryFilters()
            .Where(e => e.TenantId == tenantId);

        // Re-apply soft-delete filter manually for ISoftDeletable entities.
        // EF Core cannot selectively bypass one global query filter — IgnoreQueryFilters()
        // removes all filters, so we must re-apply the soft-delete condition explicitly.
        if (typeof(ISoftDeletable).IsAssignableFrom(typeof(TAggregate)))
        {
            query = query.Where(e => !EF.Property<bool>(e, nameof(ISoftDeletable.IsDeleted)));
        }

        return await query.FirstOrDefaultAsync(BuildIdEqualsPredicate(id), ct);
    }

    /// <summary>
    /// Retrieves an aggregate by its identity under an explicit <paramref name="tenantId"/>,
    /// bypassing both the global tenant filter and the soft-delete filter.
    /// </summary>
    /// <param name="id">The aggregate's unique identifier.</param>
    /// <param name="tenantId">The tenant scope to search within.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// The aggregate if found within the specified tenant scope, regardless of its deletion state;
    /// otherwise <see langword="null"/>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <strong>Filter semantics:</strong>
    /// <list type="bullet">
    ///   <item><description>
    ///     The <strong>tenant filter is bypassed</strong> — records are searched within the
    ///     explicitly provided <paramref name="tenantId"/>.
    ///   </description></item>
    ///   <item><description>
    ///     <strong>WARNING: The soft-delete filter is also bypassed</strong> — soft-deleted records
    ///     ARE returned. This is intentional for audit, data-export, and recovery scenarios.
    ///   </description></item>
    ///   <item><description>
    ///     For non-<see cref="ISoftDeletable"/> aggregates, this method is equivalent to
    ///     <see cref="GetByIdForTenantAsync"/>.
    ///   </description></item>
    /// </list>
    /// </para>
    /// <para>
    /// <strong>Reserved for audit, data-export, and recovery operations only.</strong>
    /// Do not use in standard application flows — use <see cref="GetByIdForTenantAsync"/> for
    /// normal cross-tenant lookups that must exclude soft-deleted records.
    /// </para>
    /// </remarks>
    public virtual async Task<TAggregate?> GetByIdForTenantIncludingDeletedAsync(
        TId id,
        Guid tenantId,
        CancellationToken ct = default)
    {
        return await DbContext.Set<TAggregate>()
            .IgnoreQueryFilters()
            .Where(e => e.TenantId == tenantId)
            .FirstOrDefaultAsync(BuildIdEqualsPredicate(id), ct);
    }

    // Builds e => e.Id.Equals(id) via expression trees — the same pattern already shipped in
    // EfRepository.ExistsAsync (D-29/P-099) — rather than EF.Property<TId>(e, "Id"), which can
    // silently fall back to client-side evaluation for strongly-typed IDs backed by a registered
    // ValueConverter, defeating server-side filtering (WO-051/P-316).
    private static Expression<Func<TAggregate, bool>> BuildIdEqualsPredicate(TId id)
    {
        var param = Expression.Parameter(typeof(TAggregate), "e");
        var idProperty = Expression.Property(param, "Id");
        var idConstant = Expression.Constant(id, typeof(TId));
        var equals = Expression.Equal(idProperty, idConstant);
        return Expression.Lambda<Func<TAggregate, bool>>(equals, param);
    }
}

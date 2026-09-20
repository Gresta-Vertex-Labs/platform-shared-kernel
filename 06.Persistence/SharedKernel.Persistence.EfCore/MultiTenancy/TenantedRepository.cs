using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Persistence.Abstractions.Context;
using SharedKernel.Persistence.EfCore.Diagnostics;
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
/// <para>
/// <strong>Auditable escape hatch required:</strong> both cross-tenant methods now
/// throw <see cref="InvalidOperationException"/> unless called with an active
/// <see cref="ICrossTenantScope"/> (<c>crossTenantScope.Enter()</c>) — bypassing tenant isolation is
/// only ever legal as a deliberate, attributable act, never an ambient capability every tenanted
/// repository has by default.
/// </para>
/// </remarks>
public abstract class TenantedRepository<TAggregate, TId> : EfRepository<TAggregate, TId>
    where TAggregate : class, IAggregateRoot<TId>, IHasTenant
    where TId : notnull
{
    private readonly ICrossTenantScope _crossTenantScope;

    /// <summary>
    /// Initialises a new <see cref="TenantedRepository{TAggregate, TId}"/>.
    /// </summary>
    /// <param name="dbContext">The scoped tenanted DB context.</param>
    /// <param name="crossTenantScope">
    /// The current logical call's cross-tenant bypass scope, consulted by
    /// <see cref="GetByIdForTenantAsync"/> and <see cref="GetByIdForTenantIncludingDeletedAsync"/>.
    /// </param>
    protected TenantedRepository(TenantedDbContext dbContext, ICrossTenantScope crossTenantScope)
        : base(dbContext)
    {
        _crossTenantScope = crossTenantScope;
    }

    /// <summary>
    /// Retrieves an aggregate by its identity under an explicit <paramref name="tenantId"/>,
    /// bypassing the global tenant filter while preserving the soft-delete filter for
    /// <see cref="ISoftDeletable"/> entities.
    /// </summary>
    /// <param name="id">The aggregate's unique identifier.</param>
    /// <param name="tenantId">The tenant scope to search within.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The aggregate if found within the specified tenant scope and not soft-deleted;
    /// otherwise <see langword="null"/>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <strong>Filter semantics:</strong>
    /// <list type="bullet">
    /// <item><description>
    /// The <strong>tenant filter is bypassed</strong> — records are searched within the
    /// explicitly provided <paramref name="tenantId"/>.
    /// </description></item>
    /// <item><description>
    /// The <strong>soft-delete filter is preserved</strong> for <see cref="ISoftDeletable"/>
    /// entities — soft-deleted records are excluded.
    /// </description></item>
    /// <item><description>
    /// For non-<see cref="ISoftDeletable"/> aggregates, this method is equivalent to
    /// <see cref="GetByIdForTenantIncludingDeletedAsync"/>.
    /// </description></item>
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
    /// <exception cref="InvalidOperationException">
    /// Thrown when no <see cref="ICrossTenantScope"/> is active for the current logical call.
    /// </exception>
    public virtual async Task<TAggregate?> GetByIdForTenantAsync(
        TId id,
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        RequireActiveCrossTenantScope(nameof(GetByIdForTenantAsync));

        // Selective: only the "SoftDelete" filter would ever need dropping here,
        // and this method deliberately does NOT drop it — the tenant filter is bypassed by scoping
        // to the explicit tenantId in the Where clause below, not via IgnoreQueryFilters. Passing an
        // empty filter-name array leaves every named filter active on the base query, then the
        // explicit tenantId predicate narrows it — equivalent in result to bypassing the tenant
        // filter (since it re-implements the same condition with a caller-chosen tenantId) while
        // never touching the "SoftDelete" filter at all.
        var query = DbContext.Set<TAggregate>()
            .IgnoreQueryFilters([PersistenceFilterNames.Tenant])
            .Where(BuildTenantEqualsPredicate(tenantId));

        return await query.FirstOrDefaultAsync(BuildIdEqualsPredicate(id), cancellationToken);
    }

    /// <summary>
    /// Retrieves an aggregate by its identity under an explicit <paramref name="tenantId"/>,
    /// bypassing both the global tenant filter and the soft-delete filter.
    /// </summary>
    /// <param name="id">The aggregate's unique identifier.</param>
    /// <param name="tenantId">The tenant scope to search within.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The aggregate if found within the specified tenant scope, regardless of its deletion state;
    /// otherwise <see langword="null"/>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <strong>Filter semantics:</strong>
    /// <list type="bullet">
    /// <item><description>
    /// The <strong>tenant filter is bypassed</strong> — records are searched within the
    /// explicitly provided <paramref name="tenantId"/>.
    /// </description></item>
    /// <item><description>
    /// <strong>WARNING: The soft-delete filter is also bypassed</strong> — soft-deleted records
    /// ARE returned. This is intentional for audit, data-export, and recovery scenarios.
    /// </description></item>
    /// <item><description>
    /// For non-<see cref="ISoftDeletable"/> aggregates, this method is equivalent to
    /// <see cref="GetByIdForTenantAsync"/>.
    /// </description></item>
    /// </list>
    /// </para>
    /// <para>
    /// <strong>Reserved for audit, data-export, and recovery operations only.</strong>
    /// Do not use in standard application flows — use <see cref="GetByIdForTenantAsync"/> for
    /// normal cross-tenant lookups that must exclude soft-deleted records.
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no <see cref="ICrossTenantScope"/> is active for the current logical call.
    /// </exception>
    public virtual async Task<TAggregate?> GetByIdForTenantIncludingDeletedAsync(
        TId id,
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        RequireActiveCrossTenantScope(nameof(GetByIdForTenantIncludingDeletedAsync));

        return await DbContext.Set<TAggregate>()
            .IgnoreQueryFilters([PersistenceFilterNames.Tenant, PersistenceFilterNames.SoftDelete])
            .Where(BuildTenantEqualsPredicate(tenantId))
            .FirstOrDefaultAsync(BuildIdEqualsPredicate(id), cancellationToken);
    }

    private void RequireActiveCrossTenantScope(string methodName)
    {
        if (!_crossTenantScope.IsActive)
        {
            throw new InvalidOperationException(
                $"'{methodName}' bypasses tenant isolation and requires an active " +
                $"'{nameof(ICrossTenantScope)}'. Call 'crossTenantScope.Enter()' (typically " +
                $"'using var _ = crossTenantScope.Enter();') around this call to make the bypass " +
                "explicit and attributable.");
        }
    }

    // Builds e => e.Id.Equals(holder.Id) via expression trees — the same pattern already shipped in
    // EfRepository.ExistsAsync, parameterized via a closure holder
    // rather than EF.Property<TId>(e, "Id"), which can silently fall back to client-side evaluation
    // for strongly-typed IDs backed by a registered ValueConverter, defeating server-side filtering.
    private static Expression<Func<TAggregate, bool>> BuildIdEqualsPredicate(TId id)
    {
        var holder = new IdHolder(id);
        var param = Expression.Parameter(typeof(TAggregate), "e");
        var idProperty = Expression.Property(param, "Id");
        var idValue = Expression.Field(Expression.Constant(holder), nameof(IdHolder.Id));
        var equals = Expression.Equal(idProperty, idValue);
        return Expression.Lambda<Func<TAggregate, bool>>(equals, param);
    }

    private static Expression<Func<TAggregate, bool>> BuildTenantEqualsPredicate(Guid tenantId)
    {
        var holder = new TenantHolder(tenantId);
        var param = Expression.Parameter(typeof(TAggregate), "e");
        var tenantProperty = Expression.Property(param, nameof(IHasTenant.TenantId));
        var tenantValue = Expression.Field(Expression.Constant(holder), nameof(TenantHolder.TenantId));
        var equals = Expression.Equal(tenantProperty, tenantValue);
        return Expression.Lambda<Func<TAggregate, bool>>(equals, param);
    }

    private sealed class IdHolder(TId id)
    {
        public readonly TId Id = id;
    }

    private sealed class TenantHolder(Guid tenantId)
    {
        public readonly Guid TenantId = tenantId;
    }
}

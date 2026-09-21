using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Persistence.EfCore.Diagnostics;

namespace SharedKernel.Persistence.EfCore.Conventions;

/// <summary>
/// An EF Core <see cref="IModelFinalizingConvention"/> that installs the platform's soft-delete
/// global query filter, under the well-known name <see cref="PersistenceFilterNames.SoftDelete"/>,
/// on every root <see cref="ISoftDeletable"/> entity type in the model.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Named filter:</strong> replaces the former unnamed
/// <c>builder.HasQueryFilter(e => !e.IsDeleted)</c> call
/// <c>EntityTypeConfigurationBase&lt;TEntity,TId&gt;</c> used to apply per entity type. EF Core
/// allowed only ONE query filter per entity type before named filters — <c>TenantedDbContext</c>'s
/// own tenant filter, applied afterwards in <c>OnModelCreating</c>, silently REPLACED this one for
/// every multi-tenant, soft-deletable entity, so soft-deleted rows leaked back into every tenant-scoped
/// query. Both filters now live under distinct keys
/// (<see cref="PersistenceFilterNames.SoftDelete"/>/<see cref="PersistenceFilterNames.Tenant"/>) and
/// EF Core combines every named filter on an entity type with a logical AND — neither can silently
/// evict the other again, by construction.
/// </para>
/// <para>
/// <strong>Registered unconditionally, context-wide:</strong> applied via
/// <see cref="Context.SharedKernelDbContext.ConfigureConventions"/> for every
/// <see cref="Context.SharedKernelDbContext"/> subclass — single-tenant included — so soft-delete
/// filtering no longer depends on a concrete entity configuration extending
/// <c>EntityTypeConfigurationBase&lt;TEntity,TId&gt;</c> at all.
/// </para>
/// <para>
/// <strong>Root types only:</strong> a TPH-derived (non-root) entity type shares its base type's
/// table and cannot carry its own query filter — EF Core throws if one is attempted. Only entity
/// types with <c>BaseType == null</c> are considered.
/// </para>
/// <para>
/// <strong>Zero reflection invocation:</strong> the filter body is built with
/// <see cref="Expression.Property(Expression, string)"/> against <c>entityType.ClrType</c> — the
/// same plain-<c>PropertyInfo</c>-binding technique <c>ByIdSpecification</c> and
/// <c>EfRepository.ExistsAsync</c> already use for a runtime-known CLR type — never
/// <c>MakeGenericMethod</c>. This is a one-time, model-build-time walk, not a per-query or per-row
/// cost.
/// </para>
/// </remarks>
internal sealed class SoftDeleteQueryFilterConvention : IModelFinalizingConvention
{
    /// <inheritdoc />
    public void ProcessModelFinalizing(
        IConventionModelBuilder modelBuilder,
        IConventionContext<IConventionModelBuilder> context)
    {
        foreach (var entityType in modelBuilder.Metadata.GetEntityTypes())
        {
            if (entityType.BaseType is not null)
                continue; // TPH-derived type — shares its root's filter.

            if (!typeof(ISoftDeletable).IsAssignableFrom(entityType.ClrType))
                continue;

            var parameter = Expression.Parameter(entityType.ClrType, "e");
            var isDeletedProperty = Expression.Property(parameter, nameof(ISoftDeletable.IsDeleted));
            var notDeleted = Expression.Not(isDeletedProperty);
            var lambda = Expression.Lambda(notDeleted, parameter);

            entityType.SetQueryFilter(PersistenceFilterNames.SoftDelete, lambda);
        }
    }
}

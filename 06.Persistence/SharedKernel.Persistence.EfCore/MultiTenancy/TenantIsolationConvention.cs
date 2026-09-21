using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Persistence.EfCore.Diagnostics;
using SharedKernel.Persistence.EfCore.Extensibility;

namespace SharedKernel.Persistence.EfCore.MultiTenancy;

/// <summary>
/// Makes every entity type of a <see cref="TenantedDbContext"/> tenant-isolated, after all configuration has run:
/// the named tenant query filter and the <c>TenantId</c> concurrency token on every <see cref="IHasTenant"/> root
/// type, and a model error for every other non-owned entity type not declared tenant-shared.
/// </summary>
/// <remarks>
/// <para>
/// A model-finalizing convention, so it sees entity types configured after <c>base.OnModelCreating</c>, or by a
/// context that never calls it.
/// </para>
/// <para>
/// <strong>Children are tenant data too.</strong> A child entity of an aggregate (a non-owned type with its own
/// table) without <see cref="IHasTenant"/> would be readable through its own <c>DbSet</c> across tenants and
/// writable through a detached graph. Every such type must implement <see cref="IHasTenant"/> (the save pipeline
/// stamps its <c>TenantId</c> from the caller when it is added unset) or be declared tenant-shared with
/// <see cref="TenantSharedAttribute"/> / <c>IsTenantShared()</c>. Owned types live inside their owner's row or
/// are reachable only through it; many-to-many join types (property bags) carry only keys of filtered entities.
/// </para>
/// </remarks>
internal sealed class TenantIsolationConvention(TenantedDbContext context) : IModelFinalizingConvention
{
    private static readonly PropertyInfo CurrentTenantIdProperty =
        typeof(TenantedDbContext).GetProperty(nameof(TenantedDbContext.CurrentTenantId))!;

    /// <inheritdoc />
    public void ProcessModelFinalizing(IConventionModelBuilder modelBuilder, IConventionContext<IConventionModelBuilder> conventionContext)
    {
        var violations = new List<string>();

        foreach (var entityType in modelBuilder.Metadata.GetEntityTypes())
        {
            if (entityType.IsOwned() || entityType.HasSharedClrType)
                continue;

            if (typeof(IHasTenant).IsAssignableFrom(entityType.ClrType))
            {
                if (entityType.BaseType is null)
                {
                    entityType.SetQueryFilter(PersistenceFilterNames.Tenant, BuildFilter(entityType.ClrType));
                    entityType.FindProperty(nameof(IHasTenant.TenantId))?.Builder.IsConcurrencyToken(true);
                }

                continue;
            }

            if (IsTenantShared(entityType))
                continue;

            violations.Add(entityType.DisplayName());
        }

        if (violations.Count > 0)
        {
            throw new InvalidOperationException(
                $"'{context.GetType().Name}' is a TenantedDbContext, but these entity types are not tenant-scoped: "
                + $"{string.Join(", ", violations.Select(v => $"'{v}'"))}. Every entity type of a multi-tenant model "
                + "must implement IHasTenant — child entities of an aggregate included, so their rows are filtered, "
                + "guarded and covered by row-level security like the root's (an added child with no TenantId gets the "
                + "caller's). A type that is global reference data shared by every tenant is declared with "
                + "[TenantShared] on the class or '.IsTenantShared()' in its configuration.");
        }
    }

    internal static bool IsTenantShared(IReadOnlyEntityType entityType)
    {
        for (var current = entityType; current is not null; current = current.BaseType)
        {
            if (current.FindAnnotation(PersistenceModelAnnotationNames.TenantShared)?.Value is true)
                return true;
        }

        return entityType.ClrType.GetCustomAttribute<TenantSharedAttribute>(inherit: true) is not null;
    }

    // e => this.CurrentTenantId.HasValue && e.TenantId == this.CurrentTenantId, where "this" is a captured context
    // constant that EF Core rebinds, per query, to the executing context instance. The HasValue guard makes the
    // fail-closed "no tenant resolved" case explicit.
    private LambdaExpression BuildFilter(Type clrType)
    {
        var parameter = Expression.Parameter(clrType, "e");
        var tenantId = Expression.Property(parameter, nameof(IHasTenant.TenantId));
        var current = Expression.Property(Expression.Constant(context, context.GetType()), CurrentTenantIdProperty);
        var hasValue = Expression.Property(current, nameof(Nullable<Guid>.HasValue));
        var equal = Expression.Equal(Expression.Convert(tenantId, typeof(Guid?)), current);
        return Expression.Lambda(Expression.AndAlso(hasValue, equal), parameter);
    }
}

/// <summary>Declares entity types of a <see cref="TenantedDbContext"/> tenant-shared.</summary>
public static class TenantSharedEntityTypeBuilderExtensions
{
    /// <summary>
    /// Declares <typeparamref name="TEntity"/> global reference data shared by every tenant: it needs no
    /// <see cref="IHasTenant"/>, is not filtered by tenant and its writes are not tenant-guarded. Equivalent to
    /// <see cref="TenantSharedAttribute"/> on the class.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="builder">The entity type builder.</param>
    /// <returns>The same builder.</returns>
    public static EntityTypeBuilder<TEntity> IsTenantShared<TEntity>(this EntityTypeBuilder<TEntity> builder)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.HasAnnotation(PersistenceModelAnnotationNames.TenantShared, true);
        return builder;
    }
}

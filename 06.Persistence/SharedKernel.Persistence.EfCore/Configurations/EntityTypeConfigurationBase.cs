using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SharedKernel.Domain.Abstractions;

namespace SharedKernel.Persistence.EfCore.Configurations;

/// <summary>
/// Abstract base class for EF Core entity type configurations.
/// Auto-applies primary key, concurrency token, soft-delete global query filter, audit columns,
/// and tenant column based on the interfaces implemented by <typeparamref name="TEntity"/>.
/// </summary>
/// <typeparam name="TEntity">The entity type being configured. Must be a class.</typeparam>
/// <typeparam name="TId">The primary-key type. Must be non-null.</typeparam>
/// <remarks>
/// <para>
/// <strong>Usage contract:</strong> Concrete configurations must call
/// <c>base.Configure(builder)</c> as the first statement in their
/// <c>Configure(EntityTypeBuilder&lt;TEntity&gt; builder)</c> override. Failure to do so
/// means audit columns, the soft-delete filter, and the concurrency token are not applied.
/// </para>
/// <para>
/// <strong>Primary key:</strong> Configured on <typeparamref name="TId"/> using a
/// <c>StronglyTypedIdValueConverter&lt;TId, TValue&gt;</c> when the ID type implements
/// <see cref="IStronglyTypedId{TValue}"/>. Concrete configurations may use
/// <c>ModelConfigurationBuilder</c> to register the converter globally instead.
/// </para>
/// <para>
/// <strong>Concurrency token:</strong> <c>.IsRowVersion()</c> is applied for
/// <see cref="IHasConcurrency"/> entities. The provider-specific concurrency convention
/// (<c>ConcurrencyTokenConvention</c>) will override this with the optimal strategy for the
/// active database provider (e.g., <c>xmin</c> for PostgreSQL).
/// </para>
/// <para>
/// <strong>Soft-delete filter:</strong> <c>e =&gt; !e.IsDeleted</c> is applied as a global
/// query filter for <see cref="ISoftDeletable"/> entities. Use <c>.IgnoreQueryFilters()</c>
/// to bypass for admin or audit queries.
/// </para>
/// <para>
/// <strong>Audit columns:</strong>
/// <list type="bullet">
///   <item><description>
///     <c>CreatedBy</c> (max-length 256, required) and <c>CreatedOn</c> (DateTimeOffset, required)
///     for <see cref="IHasCreatedAudit"/> entities.
///   </description></item>
///   <item><description>
///     Additionally <c>ModifiedBy</c> (nullable string) and <c>ModifiedOn</c> (nullable DateTimeOffset)
///     for <see cref="IHasAudit"/> entities.
///   </description></item>
/// </list>
/// </para>
/// <para>
/// <strong>Tenant column:</strong> <c>TenantId</c> (Guid, required) and an index on
/// <c>TenantId</c> for <see cref="IHasTenant"/> entities.
/// </para>
/// </remarks>
public abstract class EntityTypeConfigurationBase<TEntity, TId> : IEntityTypeConfiguration<TEntity>
    where TEntity : class
    where TId : notnull
{
    /// <summary>
    /// Applies the standard SharedKernel conventions to <paramref name="builder"/>.
    /// Concrete configurations must call this method first before adding entity-specific mappings.
    /// </summary>
    /// <param name="builder">The entity type builder for <typeparamref name="TEntity"/>.</param>
    public virtual void Configure(EntityTypeBuilder<TEntity> builder)
    {
        ConfigurePrimaryKey(builder);
        ConfigureConcurrencyToken(builder);
        ConfigureSoftDelete(builder);
        ConfigureAuditColumns(builder);
        ConfigureTenantColumn(builder);
    }

    // Configures the primary key on TId.
    private static void ConfigurePrimaryKey(EntityTypeBuilder<TEntity> builder)
    {
        // "Id" is the conventional PK name declared by Entity<TId>. Using a string literal
        // because IEntity<TId> is a marker interface and does not declare the Id property.
        builder.HasKey("Id");
    }

    // Applies .IsRowVersion() for IHasConcurrency entities.
    private static void ConfigureConcurrencyToken(EntityTypeBuilder<TEntity> builder)
    {
        if (typeof(IHasConcurrency).IsAssignableFrom(typeof(TEntity)))
        {
            builder.Property(nameof(IHasConcurrency.RowVersion))
                   .IsRowVersion();
        }
    }

    // Installs the global query filter e => !e.IsDeleted for ISoftDeletable entities.
    private static void ConfigureSoftDelete(EntityTypeBuilder<TEntity> builder)
    {
        if (!typeof(ISoftDeletable).IsAssignableFrom(typeof(TEntity)))
            return;

        // EF.Property<bool> accesses the mapped column by name, which is AOT-safe
        // (shadow property access — not CLR reflection on the entity type).
        builder.HasQueryFilter(e =>
            !EF.Property<bool>(e, nameof(ISoftDeletable.IsDeleted)));
    }

    // Configures audit columns for IHasCreatedAudit and IHasAudit entities.
    private static void ConfigureAuditColumns(EntityTypeBuilder<TEntity> builder)
    {
        if (typeof(IHasCreatedAudit).IsAssignableFrom(typeof(TEntity)))
        {
            builder.Property(nameof(IHasCreatedAudit.CreatedBy))
                   .HasMaxLength(256)
                   .IsRequired();

            builder.Property(nameof(IHasCreatedAudit.CreatedOn))
                   .IsRequired();
        }

        if (typeof(IHasAudit).IsAssignableFrom(typeof(TEntity)))
        {
            builder.Property(nameof(IHasAudit.ModifiedBy))
                   .HasMaxLength(256)
                   .IsRequired(false);

            builder.Property(nameof(IHasAudit.ModifiedOn))
                   .IsRequired(false);
        }
    }

    // Configures the TenantId column and index for IHasTenant entities.
    private static void ConfigureTenantColumn(EntityTypeBuilder<TEntity> builder)
    {
        if (typeof(IHasTenant).IsAssignableFrom(typeof(TEntity)))
        {
            builder.Property(nameof(IHasTenant.TenantId))
                   .IsRequired();

            builder.HasIndex(nameof(IHasTenant.TenantId));
        }
    }
}

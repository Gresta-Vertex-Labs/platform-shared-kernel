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
/// <strong>Concurrency token:</strong> <c>.IsConcurrencyToken()</c> is
/// applied for <see cref="IHasConcurrency"/> entities — a provider-neutral EF Core concept (the
/// property is included in the UPDATE <c>WHERE</c> clause) with zero assumption about server-side
/// auto-generation. The previously-documented <c>.IsRowVersion()</c> call (and the never-built
/// <c>ConcurrencyTokenConvention</c> this doc used to reference) is retired: a provider-branching
/// convention living in <c>SharedKernel.Persistence.EfCore</c> is architecturally impossible,
/// since <c>.UseXminAsConcurrencyToken()</c> is an <c>Npgsql.EntityFrameworkCore.PostgreSQL</c>
/// extension method and this package must never reference Npgsql. <c>.IsRowVersion()</c> was also
/// provably non-functional against a plain PostgreSQL <c>bytea</c> column — nothing in Postgres
/// auto-populates an arbitrary <c>bytea</c> on <c>UPDATE</c> the way SQL Server's native
/// <c>rowversion</c> type does, so the token value never changed and concurrent writes never
/// conflicted. The genuinely-working PostgreSQL mechanism (binding the property to the real
/// <c>xmin</c> system column) lives entirely in <c>SharedKernel.Persistence.PostgreSQL</c>'s
/// <c>XminConcurrencyTokenConvention</c>, which reconfigures the property this method marks.
/// </para>
/// <para>
/// <strong>Soft-delete filter:</strong> installed separately, context-wide, by
/// <c>Conventions.SoftDeleteQueryFilterConvention</c> under the named key
/// <c>Diagnostics.PersistenceFilterNames.SoftDelete</c> — no longer this class's concern, so it
/// applies even to an <see cref="ISoftDeletable"/> entity type with no
/// <see cref="EntityTypeConfigurationBase{TEntity, TId}"/> configuration at all. Use
/// <c>spec.IncludeDeleted = true</c> on a specification to bypass it; never call
/// <c>.IgnoreQueryFilters()</c> directly, which would also drop the tenant filter.
/// </para>
/// <para>
/// <strong>Audit columns:</strong>
/// <list type="bullet">
/// <item><description>
/// <c>CreatedBy</c> (max-length 256, required) and <c>CreatedOn</c> (DateTimeOffset, required)
/// for <see cref="IHasCreatedAudit"/> entities.
/// </description></item>
/// <item><description>
/// Additionally <c>ModifiedBy</c> (nullable string) and <c>ModifiedOn</c> (nullable DateTimeOffset)
/// for <see cref="IHasAudit"/> entities.
/// </description></item>
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
        ConfigureAuditColumns(builder);
        ConfigureTenantColumn(builder);
        ConfigureEventSequence(builder);
    }

    // Maps IHasVersion.Version so a loaded aggregate continues its event numbering instead of restarting at 0.
    // It is deliberately NOT a concurrency token: it changes only when an event is raised, while RowVersion
    // guards every write.
    private static void ConfigureEventSequence(EntityTypeBuilder<TEntity> builder)
    {
        if (typeof(IHasVersion).IsAssignableFrom(typeof(TEntity)))
        {
            builder.Property(nameof(IHasVersion.Version))
                .IsRequired();
        }
    }

    // Configures the primary key on TId.
    private static void ConfigurePrimaryKey(EntityTypeBuilder<TEntity> builder)
    {
        // "Id" is the conventional PK name declared by Entity<TId>. Using a string literal
        // because IEntity<TId> is a marker interface and does not declare the Id property.
        builder.HasKey("Id");
    }

    // Marks RowVersion as a provider-neutral concurrency token for IHasConcurrency entities
    // (see the ConfigureConcurrencyToken remarks above for why .IsRowVersion()
    // was retired). SharedKernel.Persistence.PostgreSQL's XminConcurrencyTokenConvention
    // reconfigures this property to bind to the real xmin system column when UsePostgreSQL()
    // is in effect.
    private static void ConfigureConcurrencyToken(EntityTypeBuilder<TEntity> builder)
    {
        if (typeof(IHasConcurrency).IsAssignableFrom(typeof(TEntity)))
        {
            builder.Property(nameof(IHasConcurrency.RowVersion))
                .IsConcurrencyToken();
        }
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

    /// <summary>
    /// Configures the <c>TenantId</c> column and index for <see cref="IHasTenant"/> entities.
    /// </summary>
    /// <param name="builder">The entity type builder for <typeparamref name="TEntity"/>.</param>
    /// <remarks>
    /// <para>
    /// <strong>Fail-closed by construction:</strong> the tenant global query filter
    /// (<c>MultiTenancy.TenantedDbContext</c>) reads <c>ICurrentTenantContext.TenantId</c>
    /// (<see cref="Nullable{T}"/>). When no tenant is resolved, that value is <see langword="null"/>
    /// and the filter returns <strong>zero rows</strong> — teams that forget to register a real
    /// tenant bridge see an empty result set immediately rather than a cross-tenant data leak. There
    /// is no longer a <see cref="Guid.Empty"/> sentinel to remember: a genuine, deliberately-assigned
    /// all-zero <see cref="Guid"/> tenant id is indistinguishable from any other tenant id.
    /// </para>
    /// </remarks>
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

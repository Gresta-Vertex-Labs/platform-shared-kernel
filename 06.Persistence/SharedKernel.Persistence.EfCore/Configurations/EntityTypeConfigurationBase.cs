using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SharedKernel.Persistence.EfCore.Configurations;

/// <summary>
/// Optional base class for entity configurations. It adds nothing an entity needs any more: audit, soft-delete,
/// tenant and version columns, the concurrency token and strongly-typed id conversion are all applied by
/// convention to every entity of a <c>SharedKernelDbContext</c>, whether its configuration derives from this
/// class, implements <see cref="IEntityTypeConfiguration{TEntity}"/> directly, or does not exist.
/// </summary>
/// <typeparam name="TEntity">The entity type being configured.</typeparam>
/// <typeparam name="TId">The primary-key type.</typeparam>
/// <remarks>Kept so existing configurations compile; new code implements <see cref="IEntityTypeConfiguration{TEntity}"/>.</remarks>
public abstract class EntityTypeConfigurationBase<TEntity, TId> : IEntityTypeConfiguration<TEntity>
    where TEntity : class
    where TId : notnull
{
    /// <summary>Configures <typeparamref name="TEntity"/>; the base sets the key to the conventional <c>Id</c>.</summary>
    /// <param name="builder">The entity type builder.</param>
    public virtual void Configure(EntityTypeBuilder<TEntity> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.HasKey("Id");
    }
}

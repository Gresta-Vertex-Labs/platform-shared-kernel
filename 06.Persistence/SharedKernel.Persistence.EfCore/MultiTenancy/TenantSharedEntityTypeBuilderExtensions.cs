using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.EfCore.Extensibility;
using SharedKernel.Persistence.EfCore.MultiTenancy;

namespace SharedKernel.Persistence.EfCore;

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

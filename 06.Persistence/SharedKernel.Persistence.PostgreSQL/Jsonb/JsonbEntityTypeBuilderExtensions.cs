using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SharedKernel.Persistence.PostgreSQL.Jsonb;

/// <summary>
/// EF Core fluent extension methods for configuring JSONB columns on PostgreSQL.
/// </summary>
public static class JsonbEntityTypeBuilderExtensions
{
    /// <summary>
    /// Configures the property identified by <paramref name="propertyExpression"/> to use
    /// the PostgreSQL <c>jsonb</c> column type.
    /// </summary>
    /// <typeparam name="TEntity">The entity type being configured.</typeparam>
    /// <typeparam name="TProperty">The property type (typically a complex object serialized as JSON).</typeparam>
    /// <param name="builder">The entity type builder.</param>
    /// <param name="propertyExpression">An expression selecting the property to configure.</param>
    /// <returns>The same <paramref name="builder"/> for fluent chaining.</returns>
    /// <remarks>
    /// STJ serialization is configured globally by Npgsql when <c>AddSharedKernelPostgreSQL</c>
    /// is called — no per-column converter registration is required.
    /// </remarks>
    public static EntityTypeBuilder<TEntity> HasJsonbColumn<TEntity, TProperty>(
        this EntityTypeBuilder<TEntity> builder,
        Expression<Func<TEntity, TProperty>> propertyExpression)
        where TEntity : class
    {
        builder.Property(propertyExpression).HasColumnType("jsonb");
        return builder;
    }
}

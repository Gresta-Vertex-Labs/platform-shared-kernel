using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SharedKernel.Persistence.PostgreSQL.Vector;

/// <summary>
/// EF Core fluent extension methods for configuring pgvector columns on PostgreSQL.
/// </summary>
public static class VectorEntityTypeBuilderExtensions
{
    /// <summary>
    /// Configures the property identified by <paramref name="propertyExpression"/> to use
    /// the PostgreSQL <c>vector(<paramref name="dimensions"/>)</c> column type.
    /// </summary>
    /// <typeparam name="TEntity">The entity type being configured.</typeparam>
    /// <typeparam name="TProperty">The property type (typically <c>float[]</c> or <c>Pgvector.Vector</c>).</typeparam>
    /// <param name="builder">The entity type builder.</param>
    /// <param name="propertyExpression">An expression selecting the property to configure.</param>
    /// <param name="dimensions">The fixed number of dimensions for this vector column (must be greater than zero).</param>
    /// <returns>The same <paramref name="builder"/> for fluent chaining.</returns>
    /// <remarks>
    /// Requires the <c>pgvector</c> PostgreSQL extension. Call <c>EnsureCreated()</c> or include
    /// <c>CREATE EXTENSION IF NOT EXISTS vector</c> in migrations.
    /// </remarks>
    public static EntityTypeBuilder<TEntity> HasVectorColumn<TEntity, TProperty>(
        this EntityTypeBuilder<TEntity> builder,
        Expression<Func<TEntity, TProperty>> propertyExpression,
        int dimensions)
        where TEntity : class
    {
        if (dimensions <= 0)
            throw new ArgumentOutOfRangeException(nameof(dimensions), "Vector dimensions must be greater than zero.");

        builder.Property(propertyExpression).HasColumnType($"vector({dimensions})");
        return builder;
    }
}

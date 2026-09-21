using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SharedKernel.Persistence.EfCore.Vectors;

/// <summary>
/// EF Core fluent extension methods for configuring pgvector columns and indexes on PostgreSQL.
/// </summary>
/// <remarks>
/// pgvector support is opt-in — see <c>PostgreSQLPersistenceExtensions.UsePostgreSQL</c>'s
/// <c>useVector</c> parameter, which both enables Npgsql's <c>vector</c> CLR-type mapping and
/// registers the <c>CREATE EXTENSION IF NOT EXISTS vector</c> model annotation automatically. Calling
/// <see cref="HasVectorColumn{TEntity}"/> without opting in produces a column EF Core cannot
/// translate a <see cref="Pgvector.Vector"/>-typed parameter for.
/// </remarks>
public static class VectorEntityTypeBuilderExtensions
{
    /// <summary>
    /// Configures the <see cref="Pgvector.Vector"/>-typed property identified by
    /// <paramref name="propertyExpression"/> to use the PostgreSQL
    /// <c>vector(<paramref name="dimensions"/>)</c> column type.
    /// </summary>
    /// <typeparam name="TEntity">The entity type being configured.</typeparam>
    /// <param name="builder">The entity type builder.</param>
    /// <param name="propertyExpression">An expression selecting the property to configure.</param>
    /// <param name="dimensions">The fixed number of dimensions for this vector column (must be greater than zero).</param>
    /// <returns>The same <paramref name="builder"/> for fluent chaining.</returns>
    /// <remarks>
    /// <strong>Not generic:</strong> deliberately fixed to <see cref="Pgvector.Vector"/> —
    /// this method used to be generic over an arbitrary <c>TProperty</c>, and nothing prevented
    /// calling it with a plain <c>float[]</c> property. Tested against a real PostgreSQL
    /// Testcontainer: EF Core's own relational model validator rejects a <c>float[]</c> property
    /// mapped via <c>HasColumnType("vector(n)")</c> outright — <c>InvalidOperationException</c>,
    /// "the database provider does not support mapping 'float[]' properties to 'vector(n)'
    /// columns" — at model-build time, before any query ever runs. There is no working <c>float[]</c>
    /// path to preserve; the fix is to make the unsupported call a compile error instead of a
    /// startup-time model-validation failure.
    /// </remarks>
    public static EntityTypeBuilder<TEntity> HasVectorColumn<TEntity>(
        this EntityTypeBuilder<TEntity> builder,
        Expression<Func<TEntity, Pgvector.Vector>> propertyExpression,
        int dimensions)
        where TEntity : class
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(dimensions, 0);

        builder.Property(propertyExpression).HasColumnType($"vector({dimensions})");
        return builder;
    }

    /// <summary>
    /// Configures the property identified by <paramref name="propertyExpression"/> to use
    /// pgvector's reduced-precision <c>halfvec(<paramref name="dimensions"/>)</c> column type —
    /// half the storage of <c>vector</c> for a workload that can tolerate 16-bit float precision.
    /// </summary>
    /// <typeparam name="TEntity">The entity type being configured.</typeparam>
    /// <param name="builder">The entity type builder.</param>
    /// <param name="propertyExpression">An expression selecting the property to configure.</param>
    /// <param name="dimensions">The fixed number of dimensions for this vector column.</param>
    /// <returns>The same <paramref name="builder"/> for fluent chaining.</returns>
    /// <remarks>
    /// <para>
    /// <strong>Column-type mapping only.</strong> Unlike <see cref="HasVectorColumn{TEntity}"/>,
    /// <c>Pgvector.EntityFrameworkCore</c> 0.3.0 (this package's pinned version) has no dedicated
    /// <c>Halfvec</c> CLR type or ADO.NET parameter mapping — writes/reads round-trip through
    /// PostgreSQL's own implicit <c>vector</c>↔<c>halfvec</c> cast, which works for ordinary
    /// parameterized INSERT/UPDATE/SELECT but has none of <see cref="VectorOrderingExpressions"/>'s
    /// query-translation support. Upgrade to a <c>Pgvector.EntityFrameworkCore</c> version with native
    /// <c>Halfvec</c> support before relying on this for distance-ordered queries.
    /// </para>
    /// <para>
    /// <strong>Not generic:</strong> fixed to <see cref="Pgvector.Vector"/>, same as
    /// <see cref="HasVectorColumn{TEntity}"/> and for the identical reason — a plain <c>float[]</c>
    /// property mapped via <c>HasColumnType(...)</c> fails EF Core's own relational model validation
    /// outright; there was never a working non-<see cref="Pgvector.Vector"/> path this generic
    /// parameter legitimately served.
    /// </para>
    /// </remarks>
    public static EntityTypeBuilder<TEntity> HasHalfVectorColumn<TEntity>(
        this EntityTypeBuilder<TEntity> builder,
        Expression<Func<TEntity, Pgvector.Vector>> propertyExpression,
        int dimensions)
        where TEntity : class
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(dimensions, 0);

        builder.Property(propertyExpression).HasColumnType($"halfvec({dimensions})");
        return builder;
    }

    /// <summary>
    /// Adds an approximate-nearest-neighbor index over a <see cref="Pgvector.Vector"/> column, with
    /// the pgvector operator class matching <paramref name="metric"/>.
    /// </summary>
    /// <typeparam name="TEntity">The entity type being configured.</typeparam>
    /// <param name="builder">The entity type builder.</param>
    /// <param name="propertyExpression">An expression selecting the vector-typed property to index.</param>
    /// <param name="method">The index type — <see cref="VectorIndexMethod.Hnsw"/> or <see cref="VectorIndexMethod.IvfFlat"/>.</param>
    /// <param name="metric">
    /// The distance metric this index accelerates. Must match the metric a query orders by (via
    /// <see cref="VectorOrderingExpressions.ByDistance{TAggregate}"/>) for PostgreSQL's planner to
    /// actually use this index — an ORDER BY using a different operator falls back to a sequential
    /// scan.
    /// </param>
    /// <param name="indexName">Optional explicit index name; a default is generated when omitted.</param>
    /// <returns>The same <paramref name="builder"/> for fluent chaining.</returns>
    /// <remarks>
    /// An IVFFlat index built against an empty table has no useful list centroids — build it AFTER
    /// the table has representative data (e.g. in a follow-up migration, or by rebuilding
    /// periodically), never in the same migration that creates the table.
    /// </remarks>
    public static EntityTypeBuilder<TEntity> HasVectorIndex<TEntity>(
        this EntityTypeBuilder<TEntity> builder,
        Expression<Func<TEntity, object?>> propertyExpression,
        VectorIndexMethod method,
        VectorDistanceMetric metric,
        string? indexName = null)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(propertyExpression);

        var indexBuilder = indexName is null
            ? builder.HasIndex(propertyExpression)
            : builder.HasIndex(propertyExpression, indexName);

        indexBuilder
            .HasMethod(method == VectorIndexMethod.Hnsw ? "hnsw" : "ivfflat")
            .HasOperators([OperatorClassFor(metric)]);

        return builder;
    }

    private static string OperatorClassFor(VectorDistanceMetric metric) => metric switch
    {
        VectorDistanceMetric.Cosine => "vector_cosine_ops",
        VectorDistanceMetric.L2 => "vector_l2_ops",
        VectorDistanceMetric.L1 => "vector_l1_ops",
        VectorDistanceMetric.InnerProduct => "vector_ip_ops",
        _ => throw new ArgumentOutOfRangeException(nameof(metric), metric, "Unsupported vector distance metric."),
    };
}

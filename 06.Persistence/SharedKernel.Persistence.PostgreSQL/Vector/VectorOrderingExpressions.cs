using System.Linq.Expressions;
using Pgvector.EntityFrameworkCore;
using PgVector = Pgvector.Vector;

namespace SharedKernel.Persistence.PostgreSQL.Vector;

/// <summary>
/// Builds nearest-neighbor <c>OrderBy</c> expressions for <see cref="Pgvector.Vector"/>-typed columns.
/// </summary>
/// <remarks>
/// <para>
/// The platform's first query-side pgvector ergonomics; <c>HasVectorColumn</c>/
/// <c>VectorColumnAttribute</c> map only a column type, not even routed through
/// <c>Pgvector.EntityFrameworkCore</c>'s own helper types. This helper is the first assist for the
/// one thing a consumer actually wants to do with a vector column — find the nearest rows to a
/// query vector.
/// </para>
/// <para>
/// <strong>Local alias, not a public-surface change:</strong> this file aliases
/// <see cref="Pgvector.Vector"/> as <c>PgVector</c> purely because this type's own enclosing
/// namespace, <c>SharedKernel.Persistence.PostgreSQL.Vector</c>, collides with the bare simple name
/// <c>Vector</c> (confirmed by direct compilation — the C# compiler resolves an unqualified
/// <c>Vector</c> parameter-type reference against the enclosing namespace segment before the
/// imported <see cref="Pgvector.Vector"/> type, producing <c>CS0118</c>), the usual collision between a
/// namespace segment and a type of the same simple name. The public API surface below is
/// unaffected — <c>PgVector</c> is exactly <see cref="Pgvector.Vector"/>, spelled differently only
/// inside this file.
/// </para>
/// <para>
/// <strong>Zero runtime reflection:</strong> the <see cref="System.Reflection.MethodInfo"/> for
/// each distance function is captured via a statically-typed delegate cast —
/// <c>((Func&lt;PgVector,PgVector,double&gt;)VectorDbFunctionsExtensions.CosineDistance).Method</c>
/// — resolved entirely by the C# compiler, never <c>Type.GetMethod</c>/<c>MakeGenericMethod</c> at
/// runtime. This works despite the real methods' <c>(object, object)</c> signature because
/// <see cref="Pgvector.Vector"/> is a reference type — confirmed via reflection — and C#'s
/// method-group-to-delegate conversion permits contravariant reference-type parameter widening (a
/// method accepting <c>object</c> satisfies a <c>Func&lt;PgVector,PgVector,double&gt;</c> delegate
/// site); independently compiled and confirmed against the real shipped
/// <c>Pgvector.EntityFrameworkCore</c> 0.3.0 package to produce the expected
/// <see cref="System.Reflection.MethodInfo"/>, not merely assumed from the C# language spec. The
/// captured <see cref="System.Reflection.MethodInfo"/>'s own parameters report <c>object</c>/
/// <c>object</c>, not <see cref="Pgvector.Vector"/>/<see cref="Pgvector.Vector"/> —
/// <see cref="Expression.Call(System.Reflection.MethodInfo, Expression, Expression)"/>'s argument
/// type-checking accepts the <see cref="Pgvector.Vector"/>-typed selector/query-vector expressions
/// directly against those <c>object</c> parameters via the identical implicit reference-conversion
/// rule, so no <see cref="Expression.Convert(Expression, Type)"/>/boxing node is needed for the
/// arguments.
/// </para>
/// <para>
/// The real, sealed, static distance/similarity type is
/// <see cref="Pgvector.EntityFrameworkCore.VectorDbFunctionsExtensions"/> — confirmed via direct
/// reflection against the shipped assembly; it exposes SIX members
/// (<c>CosineDistance</c>/<c>L2Distance</c>/<c>L1Distance</c>/<c>HammingDistance</c>/
/// <c>JaccardDistance</c>/<c>MaxInnerProduct</c>), each an EF-Core query-translation placeholder
/// method meaningfully invoked only inside a LINQ expression tree — never client-side. This helper
/// narrows to the four metrics pgvector's own index operator classes support (Cosine/L2/L1/
/// InnerProduct) via <see cref="VectorDistanceMetric"/>; <c>HammingDistance</c>/<c>JaccardDistance</c>
/// apply only to <c>bit</c>-vector columns and remain out of scope.
/// </para>
/// <para>
/// Deliberately does NOT call any <c>ApplyOrderBy</c>/<c>AddOrderBy</c> builder method itself and
/// introduces no new <c>Specification&lt;T&gt;</c> base class — the returned expression is passed
/// by the CONSUMER's own <c>Specification&lt;TAggregate&gt;</c> subclass into its own protected
/// <c>ApplyOrderBy</c> call from within that subclass's constructor, exactly how
/// <c>KeysetSpecification&lt;T,TKey&gt;</c>/<c>PagedSpecification&lt;T&gt;</c> already populate
/// protected members from their own constructors. Composes automatically with the consumer's own
/// <c>Criteria</c>/<c>Includes</c>/<c>Take</c> — zero <c>SpecificationEvaluator&lt;T&gt;</c>
/// pipeline changes, since nothing about the evaluator's existing <c>OrderBy</c>→...→<c>Take</c>
/// handling changes.
/// </para>
/// <para>
/// Scoped to <see cref="Pgvector.Vector"/>-typed properties — the only type
/// <c>HasVectorColumn{TEntity}</c> accepts (narrowed it from a generic <c>TProperty</c>
/// after confirming a plain <c>float[]</c> column never worked at all).
/// </para>
/// </remarks>
public static class VectorOrderingExpressions
{
    private static readonly System.Reflection.MethodInfo CosineDistanceMethod =
        ((Func<PgVector, PgVector, double>)VectorDbFunctionsExtensions.CosineDistance).Method;

    private static readonly System.Reflection.MethodInfo L2DistanceMethod =
        ((Func<PgVector, PgVector, double>)VectorDbFunctionsExtensions.L2Distance).Method;

    private static readonly System.Reflection.MethodInfo L1DistanceMethod =
        ((Func<PgVector, PgVector, double>)VectorDbFunctionsExtensions.L1Distance).Method;

    private static readonly System.Reflection.MethodInfo MaxInnerProductMethod =
        ((Func<PgVector, PgVector, double>)VectorDbFunctionsExtensions.MaxInnerProduct).Method;

    /// <summary>
    /// Builds a boxed-to-<see cref="object"/> ordering key-selector expression that orders
    /// <typeparamref name="TAggregate"/> rows by distance from <paramref name="queryVector"/>,
    /// suitable for passing directly into a <c>Specification&lt;TAggregate&gt;</c> subclass's own
    /// <c>ApplyOrderBy</c> call.
    /// </summary>
    /// <typeparam name="TAggregate">The entity type being ordered.</typeparam>
    /// <param name="vectorSelector">Selects the <see cref="Pgvector.Vector"/>-typed column to compare against.</param>
    /// <param name="queryVector">The query vector to measure distance from.</param>
    /// <param name="metric">The distance/similarity metric to use.</param>
    /// <returns>
    /// An <c>Expression&lt;Func&lt;TAggregate, object&gt;&gt;</c> matching the shape
    /// <c>ISpecification{T}.OrderBy</c>/<c>OrderByDescending</c> already expect.
    /// </returns>
    /// <example>
    /// <code>
    /// public sealed class NearestProductsSpecification: Specification&lt;Product&gt;
    /// {
    /// public NearestProductsSpecification(Vector queryEmbedding, int topK)
    /// {
    /// AddCriteria(p =&gt; p.IsActive);
    /// ApplyOrderBy(VectorOrderingExpressions.ByDistance&lt;Product&gt;(
    /// p =&gt; p.Embedding, queryEmbedding, VectorDistanceMetric.Cosine));
    /// ApplyPaging(skip: 0, take: topK);
    /// }
    /// }
    /// </code>
    /// </example>
    public static Expression<Func<TAggregate, object>> ByDistance<TAggregate>(
        Expression<Func<TAggregate, PgVector>> vectorSelector,
        PgVector queryVector,
        VectorDistanceMetric metric)
    {
        ArgumentNullException.ThrowIfNull(vectorSelector);
        ArgumentNullException.ThrowIfNull(queryVector);

        var distanceMethod = metric switch
        {
            VectorDistanceMetric.Cosine => CosineDistanceMethod,
            VectorDistanceMetric.L2 => L2DistanceMethod,
            VectorDistanceMetric.L1 => L1DistanceMethod,
            VectorDistanceMetric.InnerProduct => MaxInnerProductMethod,
            _ => throw new ArgumentOutOfRangeException(nameof(metric), metric, "Unsupported vector distance metric."),
        };

        // Deliberately NOT Expression.Constant(queryVector, typeof(PgVector)) directly: a bare
        // ConstantExpression is treated by EF Core's query pipeline as an already-evaluated INLINE
        // SQL literal, rendered via Vector.ToString() ("[1,2,3]") with no quoting/cast — confirmed
        // empirically to produce a genuine PostgresException ("syntax error at or near '['") when
        // executed, since Pgvector.EntityFrameworkCore's distance-function SQL translator does not
        // attach a "vector" RelationalTypeMapping to an already-bare constant argument the way it
        // does for a genuine query PARAMETER. Wrapping the value inside a tiny holder and accessing
        // it via a MemberExpression reproduces the EXACT shape the C# compiler emits for a captured
        // local variable inside an ordinary LINQ lambda closure (a display-class instance held as a
        // ConstantExpression, read via member access) — the shape EF Core's own parameter-extraction
        // visitor recognizes and promotes to a genuine ADO.NET query parameter, which Npgsql then
        // writes through its normal, already-proven-working Vector parameter type mapping (the same
        // path every INSERT/UPDATE of a Vector-typed column already uses).
        var queryVectorHolder = new VectorQueryParameterHolder(queryVector);
        var queryVectorAccess = Expression.Property(
            Expression.Constant(queryVectorHolder),
            nameof(VectorQueryParameterHolder.Value));

        var distanceCall = Expression.Call(distanceMethod, vectorSelector.Body, queryVectorAccess);
        var boxed = Expression.Convert(distanceCall, typeof(object));

        return Expression.Lambda<Func<TAggregate, object>>(boxed, vectorSelector.Parameters[0]);
    }

    // Deliberately a private, single-property holder — its ONLY purpose is to give the query
    // vector value the same "MemberExpression over a closure-held ConstantExpression" shape EF
    // Core's parameter-extraction visitor already recognizes for genuine LINQ closures. Never
    // exposed, never reused for any other purpose.
    private sealed class VectorQueryParameterHolder(PgVector value)
    {
        public PgVector Value { get; } = value;
    }
}

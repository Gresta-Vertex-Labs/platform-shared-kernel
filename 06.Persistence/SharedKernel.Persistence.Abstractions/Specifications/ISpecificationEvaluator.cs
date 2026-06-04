using System.Linq;
using SharedKernel.Domain.Specifications;

namespace SharedKernel.Persistence.Abstractions.Specifications;

/// <summary>
/// Translates an <see cref="ISpecification{T}"/> into an <see cref="IQueryable{T}"/> pipeline.
/// </summary>
/// <typeparam name="T">The entity type this evaluator operates on.</typeparam>
/// <remarks>
/// <para>
/// This interface lives in <c>SharedKernel.Persistence.Abstractions</c> so that alternative
/// evaluator implementations (e.g., for Cosmos DB, in-memory collections, or Marten) can
/// implement the same contract without coupling to EF Core.
/// </para>
/// <para>
/// The canonical EF Core implementation (<c>SpecificationEvaluator&lt;T&gt;</c> in
/// <c>SharedKernel.Persistence.EfCore</c>) applies operations in this strict order:
/// <list type="number">
///   <item><description>IgnoreQueryFilters — only when <c>spec.IncludeDeleted == true</c>; applied before all other steps</description></item>
///   <item><description>Criteria (Where clause — <see langword="null"/> matches all entities)</description></item>
///   <item><description>Includes (expression-based Include / ThenInclude eager loading)</description></item>
///   <item><description>StringIncludes (string-based Include paths — applied after expression includes, before ordering; e.g. <c>"Orders.Items.Product"</c>; empty list is a no-op)</description></item>
///   <item><description>OrderBy / OrderByDescending (primary sort)</description></item>
///   <item><description>ThenBys (secondary sorts — only when a primary sort is set)</description></item>
///   <item><description>Distinct</description></item>
///   <item><description>AsNoTracking</description></item>
///   <item><description>Skip / Take — paging; <strong>always the final operation before projection</strong></description></item>
///   <item><description>Select(spec.Selector) — projection overload only; applied after Skip/Take</description></item>
/// </list>
/// Alternative evaluators must preserve the paging-last invariant and implement both methods.
/// All implementations must read both <c>spec.Includes</c> (expression-based) and
/// <c>spec.StringIncludes</c> (string-based) — omitting <c>StringIncludes</c> is a silent bug.
/// </para>
/// <para>
/// <strong>Breaking change (P-097):</strong> <c>GetProjectedQuery</c> has been promoted from
/// the concrete <c>SpecificationEvaluator&lt;T&gt;</c> to this interface. All alternative
/// evaluator implementations must implement <c>GetProjectedQuery</c>. Downcasting
/// <c>ISpecificationEvaluator&lt;T&gt;</c> to the concrete type to reach <c>GetProjectedQuery</c>
/// is a hard violation.
/// </para>
/// </remarks>
public interface ISpecificationEvaluator<T>
{
    /// <summary>
    /// Applies the rules encoded in <paramref name="spec"/> to <paramref name="inputQuery"/>
    /// and returns the resulting query.
    /// </summary>
    /// <param name="inputQuery">
    /// The base <see cref="IQueryable{T}"/> produced by the repository (e.g.,
    /// <c>DbContext.Set&lt;T&gt;()</c>).
    /// </param>
    /// <param name="spec">The specification to apply.</param>
    /// <returns>
    /// A new <see cref="IQueryable{T}"/> with all specification rules composed in.
    /// The returned query has not been executed — callers materialise it with
    /// <c>ToListAsync</c>, <c>FirstOrDefaultAsync</c>, etc.
    /// </returns>
    IQueryable<T> GetQuery(IQueryable<T> inputQuery, ISpecification<T> spec);

    /// <summary>
    /// Applies the full aggregate specification pipeline (criteria, includes, ordering, paging)
    /// to <paramref name="inputQuery"/>, then applies the projection selector from
    /// <paramref name="spec"/> as the final step.
    /// </summary>
    /// <typeparam name="TResult">
    /// The projection output type. Must be translatable by the underlying query provider.
    /// </typeparam>
    /// <param name="inputQuery">
    /// The base <see cref="IQueryable{T}"/> produced by the repository (e.g.,
    /// <c>DbContext.Set&lt;T&gt;()</c>).
    /// </param>
    /// <param name="spec">
    /// The projection specification supplying filter criteria, ordering, paging, and the
    /// <c>Selector</c> expression applied after paging.
    /// </param>
    /// <returns>
    /// A new <see cref="IQueryable{TResult}"/> with the full pipeline and projection applied.
    /// The returned query has not been executed.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The pipeline order is identical to <see cref="GetQuery"/>. The projection step
    /// (<c>Select(spec.Selector)</c>) is applied <strong>after</strong> Skip/Take to preserve
    /// the paging-last invariant.
    /// </para>
    /// <para>
    /// This method is on the interface (since P-097) so that alternative evaluator implementations
    /// (Cosmos, in-memory, Marten) implement the same contract. Any downcast to the concrete
    /// <c>SpecificationEvaluator&lt;T&gt;</c> type to reach this method is a hard violation.
    /// </para>
    /// </remarks>
    IQueryable<TResult> GetProjectedQuery<TResult>(
        IQueryable<T> inputQuery,
        IProjectionSpecification<T, TResult> spec);
}

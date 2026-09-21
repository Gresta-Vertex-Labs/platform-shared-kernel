using SharedKernel.Domain.Specifications;

namespace SharedKernel.Persistence.EfCore.Specifications;

/// <summary>
/// Translates an <see cref="ISpecification{T}"/> into an <see cref="IQueryable{T}"/> pipeline.
/// </summary>
/// <typeparam name="T">The entity type this evaluator operates on.</typeparam>
/// <remarks>
/// <para>
/// The EF Core implementation, <see cref="SpecificationEvaluator{T}"/>, applies in this order: <c>TagWith</c>,
/// the selective soft-delete filter bypass, criteria, includes, string includes, split-query, Distinct,
/// ordering, then Skip/Take (which require a primary sort). A projection is applied last.
/// </para>
/// <para>
/// <b>Tracking is not the evaluator's concern.</b> The repository applies no-tracking (read side) or tracking
/// (write side) to the returned query.
/// </para>
/// </remarks>
public interface ISpecificationEvaluator<T>
{
    /// <summary>Applies <paramref name="spec"/> to <paramref name="inputQuery"/>.</summary>
    /// <param name="inputQuery">The base query, typically <c>DbContext.Set&lt;T&gt;()</c>.</param>
    /// <param name="spec">The specification to apply.</param>
    /// <returns>The composed, not yet executed query.</returns>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="spec"/> declares Skip/Take without a primary sort.
    /// </exception>
    IQueryable<T> GetQuery(IQueryable<T> inputQuery, ISpecification<T> spec);

    /// <summary>Applies <paramref name="spec"/> and then its projection selector.</summary>
    /// <typeparam name="TResult">The projected type.</typeparam>
    /// <param name="inputQuery">The base query, typically <c>DbContext.Set&lt;T&gt;()</c>.</param>
    /// <param name="spec">The projection specification.</param>
    /// <returns>The composed, not yet executed projected query.</returns>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="spec"/> declares Skip/Take without a primary sort.
    /// </exception>
    IQueryable<TResult> GetProjectedQuery<TResult>(IQueryable<T> inputQuery, IProjectionSpecification<T, TResult> spec);
}

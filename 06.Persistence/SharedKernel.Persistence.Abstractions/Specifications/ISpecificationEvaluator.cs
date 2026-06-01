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
///   <item><description>Criteria (Where clause — <see langword="null"/> matches all entities)</description></item>
///   <item><description>Includes (Include / ThenInclude eager loading)</description></item>
///   <item><description>OrderBy / OrderByDescending (primary sort)</description></item>
///   <item><description>ThenBys (secondary sorts — only when a primary sort is set)</description></item>
///   <item><description>Distinct</description></item>
///   <item><description>AsNoTracking</description></item>
///   <item><description>Skip / Take — paging; <strong>always the final operation</strong></description></item>
/// </list>
/// Alternative evaluators must preserve the paging-last invariant.
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
}

using Microsoft.EntityFrameworkCore;
using SharedKernel.Domain.Specifications;
using SharedKernel.Persistence.Abstractions.Specifications;

namespace SharedKernel.Persistence.EfCore.Specifications;

/// <summary>
/// EF Core implementation of <see cref="ISpecificationEvaluator{T}"/>.
/// Translates an <see cref="ISpecification{T}"/> into a composable <see cref="IQueryable{T}"/>
/// pipeline in the following strict order:
/// <list type="number">
///   <item><description>IgnoreQueryFilters — only when <c>spec.IncludeDeleted == true</c>; applied before all other steps</description></item>
///   <item><description>Criteria (Where clause — <see langword="null"/> matches all entities)</description></item>
///   <item><description>Includes (Include / ThenInclude eager loading)</description></item>
///   <item><description>OrderBy / OrderByDescending (primary sort)</description></item>
///   <item><description>ThenBys (secondary sorts — only when a primary sort is set)</description></item>
///   <item><description>Distinct</description></item>
///   <item><description>AsNoTracking</description></item>
///   <item><description>Skip / Take — paging; <strong>always the final operation</strong> for the aggregate pipeline</description></item>
///   <item><description>Select(spec.Selector) — projection overload only; applied after Skip/Take</description></item>
/// </list>
/// </summary>
/// <typeparam name="T">The entity type this evaluator operates on.</typeparam>
/// <remarks>
/// <para>
/// The paging-last invariant ensures that ordering is stable before any Skip/Take is applied.
/// ThenBy entries are silently ignored when no primary sort has been configured.
/// </para>
/// <para>
/// <strong>Breaking change (P-080):</strong> <c>QueryableExtensions.IgnoreSoftDeleteFilter()</c>
/// has been removed. Use <c>spec.IncludeDeleted = true</c> instead — this evaluator calls
/// <c>.IgnoreQueryFilters()</c> automatically at step 0.
/// </para>
/// </remarks>
public sealed class SpecificationEvaluator<T> : ISpecificationEvaluator<T>
    where T : class
{
    /// <inheritdoc />
    public IQueryable<T> GetQuery(IQueryable<T> inputQuery, ISpecification<T> spec)
    {
        var query = inputQuery;

        // 0. IgnoreQueryFilters — bypasses ALL global query filters (soft-delete + tenant).
        //    Applied before Criteria to prevent filter interference.
        if (spec.IncludeDeleted)
            query = query.IgnoreQueryFilters();

        // 1. Criteria
        if (spec.Criteria is not null)
            query = query.Where(spec.Criteria);

        // 2. Includes
        query = spec.Includes.Aggregate(query,
            (current, include) => current.Include(include));

        // 3. Primary sort
        var hasPrimarySort = false;
        IOrderedQueryable<T>? ordered = null;

        if (spec.OrderBy is not null)
        {
            ordered = query.OrderBy(spec.OrderBy);
            hasPrimarySort = true;
        }
        else if (spec.OrderByDescending is not null)
        {
            ordered = query.OrderByDescending(spec.OrderByDescending);
            hasPrimarySort = true;
        }

        // 4. ThenBys — only when primary sort is set
        if (hasPrimarySort && ordered is not null)
        {
            foreach (var (keySelector, descending) in spec.ThenBys)
            {
                ordered = descending
                    ? ordered.ThenByDescending(keySelector)
                    : ordered.ThenBy(keySelector);
            }

            query = ordered;
        }

        // 5. Distinct
        if (spec.IsDistinct)
            query = query.Distinct();

        // 6. AsNoTracking
        if (spec.AsNoTracking)
            query = query.AsNoTracking();

        // 7. Skip / Take — ALWAYS LAST for the aggregate pipeline
        if (spec.Skip.HasValue)
            query = query.Skip(spec.Skip.Value);

        if (spec.Take.HasValue)
            query = query.Take(spec.Take.Value);

        return query;
    }

    /// <summary>
    /// Applies the full aggregate pipeline (steps 0–7) then projects the result using the
    /// <paramref name="spec"/>'s selector (step 8).
    /// </summary>
    /// <typeparam name="TResult">The projection output type.</typeparam>
    /// <param name="inputQuery">The base queryable to build upon.</param>
    /// <param name="spec">The projection specification supplying both the pipeline and the selector.</param>
    /// <returns>
    /// An <see cref="IQueryable{TResult}"/> with Select applied after all aggregate pipeline steps.
    /// </returns>
    /// <remarks>
    /// Selector is applied at step 8 — after Skip/Take — to preserve the paging-last invariant.
    /// </remarks>
    public IQueryable<TResult> GetProjectedQuery<TResult>(
        IQueryable<T> inputQuery,
        IProjectionSpecification<T, TResult> spec)
    {
        // Apply the full aggregate pipeline (steps 0–7).
        var query = GetQuery(inputQuery, spec);

        // Step 8 — Select(spec.Selector): applied after paging.
        return query.Select(spec.Selector);
    }
}

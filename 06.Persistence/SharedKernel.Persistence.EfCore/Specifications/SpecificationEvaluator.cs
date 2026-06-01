using Microsoft.EntityFrameworkCore;
using SharedKernel.Domain.Specifications;
using SharedKernel.Persistence.Abstractions.Specifications;

namespace SharedKernel.Persistence.EfCore.Specifications;

/// <summary>
/// EF Core implementation of <see cref="ISpecificationEvaluator{T}"/>.
/// Translates an <see cref="ISpecification{T}"/> into a composable <see cref="IQueryable{T}"/>
/// pipeline in the following strict order:
/// <list type="number">
///   <item><description>Criteria (Where clause — <see langword="null"/> matches all entities)</description></item>
///   <item><description>Includes (Include / ThenInclude eager loading)</description></item>
///   <item><description>OrderBy / OrderByDescending (primary sort)</description></item>
///   <item><description>ThenBys (secondary sorts — only when a primary sort is set)</description></item>
///   <item><description>Distinct</description></item>
///   <item><description>AsNoTracking</description></item>
///   <item><description>Skip / Take — paging; <strong>always the final operation</strong></description></item>
/// </list>
/// </summary>
/// <typeparam name="T">The entity type this evaluator operates on.</typeparam>
/// <remarks>
/// The paging-last invariant ensures that ordering is stable before any Skip/Take is applied.
/// ThenBy entries are silently ignored when no primary sort has been configured.
/// </remarks>
public sealed class SpecificationEvaluator<T> : ISpecificationEvaluator<T>
    where T : class
{
    /// <inheritdoc />
    public IQueryable<T> GetQuery(IQueryable<T> inputQuery, ISpecification<T> spec)
    {
        var query = inputQuery;

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

        // 7. Skip / Take — ALWAYS LAST
        if (spec.Skip.HasValue)
            query = query.Skip(spec.Skip.Value);

        if (spec.Take.HasValue)
            query = query.Take(spec.Take.Value);

        return query;
    }
}

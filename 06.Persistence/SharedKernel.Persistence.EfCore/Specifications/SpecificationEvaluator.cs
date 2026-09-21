using Microsoft.EntityFrameworkCore;
using SharedKernel.Domain.Specifications;
using SharedKernel.Persistence.EfCore.Diagnostics;

namespace SharedKernel.Persistence.EfCore.Specifications;

/// <summary>
/// EF Core implementation of <see cref="ISpecificationEvaluator{T}"/>.
/// </summary>
/// <typeparam name="T">The entity type this evaluator operates on.</typeparam>
/// <remarks>
/// <para>
/// Applies, in this order:
/// <list type="number">
/// <item><description><c>TagWith(spec type name)</c>, so a slow query in the database log names its specification.</description></item>
/// <item><description><c>IgnoreQueryFilters(["SoftDelete"])</c> when <see cref="ISpecification{T}.IncludeDeleted"/> is set. The
/// tenant filter is never dropped here; crossing tenants needs the explicit cross-tenant scope.</description></item>
/// <item><description>Criteria.</description></item>
/// <item><description>Includes, then string includes (which also hold typed <c>ThenInclude</c> paths), then
/// <c>AsSplitQuery()</c> when requested.</description></item>
/// <item><description>Distinct, before ordering, so the SQL computes the distinct set and then orders it.</description></item>
/// <item><description>Primary sort, then each secondary key.</description></item>
/// <item><description>Skip / Take, last; paging without a primary sort throws rather than returning a
/// database-dependent order.</description></item>
/// <item><description>The projection (projection overload only), after paging.</description></item>
/// </list>
/// </para>
/// <para>Stateless; register it as a singleton.</para>
/// </remarks>
internal sealed class SpecificationEvaluator<T> : ISpecificationEvaluator<T>
    where T : class
{
    /// <inheritdoc />
    public IQueryable<T> GetQuery(IQueryable<T> inputQuery, ISpecification<T> spec)
    {
        ArgumentNullException.ThrowIfNull(inputQuery);
        ArgumentNullException.ThrowIfNull(spec);

        var query = inputQuery.TagWith(spec.GetType().Name);

        if (spec.IncludeDeleted)
            query = query.IgnoreQueryFilters([PersistenceFilterNames.SoftDelete]);

        if (spec.Criteria is not null)
            query = query.Where(spec.Criteria);

        foreach (var include in spec.Includes)
            query = query.Include(include);

        foreach (var path in spec.StringIncludes)
        {
            if (!string.IsNullOrWhiteSpace(path))
                query = query.Include(path);
        }

        if (spec.AsSplitQuery)
            query = query.AsSplitQuery();

        if (spec.IsDistinct)
            query = query.Distinct();

        var hasPrimarySort = TryOrder(ref query, spec);

        if (spec.Skip.HasValue || spec.Take.HasValue)
        {
            if (!hasPrimarySort)
            {
                throw new InvalidOperationException(
                    $"'{spec.GetType().Name}' declares Skip/Take but no primary sort. Paging without a "
                    + "deterministic order returns a database-dependent row order; add OrderBy/OrderByDescending.");
            }

            if (spec.Skip.HasValue)
                query = query.Skip(spec.Skip.Value);

            if (spec.Take.HasValue)
                query = query.Take(spec.Take.Value);
        }

        return query;
    }

    /// <inheritdoc />
    public IQueryable<TResult> GetProjectedQuery<TResult>(IQueryable<T> inputQuery, IProjectionSpecification<T, TResult> spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        return GetQuery(inputQuery, spec).Select(spec.Selector);
    }

    private static bool TryOrder(ref IQueryable<T> query, ISpecification<T> spec)
    {
        IOrderedQueryable<T> ordered;

        if (spec.OrderBy is not null)
            ordered = query.OrderBy(spec.OrderBy);
        else if (spec.OrderByDescending is not null)
            ordered = query.OrderByDescending(spec.OrderByDescending);
        else
            return false;

        foreach (var (keySelector, descending) in spec.ThenBys)
            ordered = descending ? ordered.ThenByDescending(keySelector) : ordered.ThenBy(keySelector);

        query = ordered;
        return true;
    }
}

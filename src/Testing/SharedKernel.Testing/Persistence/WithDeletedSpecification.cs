using System.Linq.Expressions;
using SharedKernel.Domain.Specifications;

namespace SharedKernel.Testing.Persistence;

/// <summary>
/// Wraps an existing <see cref="ISpecification{TAggregate}"/> with <c>IncludeDeleted = true</c>
/// for soft-delete integration tests, without mutating the original specification instance.
/// </summary>
/// <typeparam name="TAggregate">The aggregate root type the specification queries.</typeparam>
public sealed class WithDeletedSpecification<TAggregate> : Specification<TAggregate>
{
    private WithDeletedSpecification(ISpecification<TAggregate> inner)
    {
        if (inner.Criteria is not null)
            AddCriteria(inner.Criteria);

        foreach (var include in inner.Includes)
            AddInclude(include);

        CopyPrimaryOrdering(inner);

        foreach (var (keySelector, descending) in inner.ThenBys)
        {
            if (descending)
                ApplyThenByDescending(keySelector);
            else
                ApplyThenBy(keySelector);
        }

        if (inner.Skip is int skip && inner.Take is int take)
            ApplyPaging(skip, take);
        else if (inner.Take is int takeOnly)
            ApplyTake(takeOnly);

        if (inner.IsDistinct)
            ApplyDistinct();

        foreach (var path in inner.StringIncludes)
            AddStringInclude(path);

        IncludeSoftDeleted();
    }

    // A specification carries exactly one primary sort direction; further keys arrive through ThenBys.
    private void CopyPrimaryOrdering(ISpecification<TAggregate> inner)
    {
        if (inner.OrderBy is not null)
            ApplyOrderBy(inner.OrderBy);
        else if (inner.OrderByDescending is not null)
            ApplyOrderByDescending(inner.OrderByDescending);
    }

    /// <summary>
    /// Returns a copy of <paramref name="inner"/> with <c>IncludeDeleted = true</c>. The original
    /// specification instance is left untouched.
    /// </summary>
    /// <param name="inner">The specification to wrap.</param>
    /// <returns>A new <see cref="ISpecification{TAggregate}"/> equivalent to <paramref name="inner"/> but soft-delete-inclusive.</returns>
    public static ISpecification<TAggregate> Wrap(ISpecification<TAggregate> inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        return new WithDeletedSpecification<TAggregate>(inner);
    }
}

using System.Linq.Expressions;
using SharedKernel.Domain.Specifications;

namespace SharedKernel.Persistence.EfCore.Repositories;

/// <summary>Checks that a specification can be paged by a page request supplied at the call site.</summary>
internal static class PagingGuard
{
    /// <summary>Offset paging: the specification needs a primary sort and must not page itself.</summary>
    /// <exception cref="InvalidOperationException">It has no primary sort or declares Skip/Take.</exception>
    internal static void EnsureOffsetPageable<T>(ISpecification<T> spec, string method)
    {
        ArgumentNullException.ThrowIfNull(spec);
        EnsureNotSelfPaged(spec, method);

        if (spec.OrderBy is null && spec.OrderByDescending is null)
        {
            throw new InvalidOperationException(
                $"'{spec.GetType().Name}' has no primary sort, so '{method}' cannot return stable pages. Order by a "
                + "key that ends in a unique column, such as .OrderByDescending(o => o.CreatedOn).ThenBy(o => o.Id).");
        }
    }

    /// <summary>Keyset paging: the key and identity order the page, so the specification must not order or page.</summary>
    /// <exception cref="InvalidOperationException">It declares ordering or Skip/Take.</exception>
    internal static void EnsureKeysetPageable<T>(ISpecification<T> spec, string method)
    {
        ArgumentNullException.ThrowIfNull(spec);
        EnsureNotSelfPaged(spec, method);

        if (spec.OrderBy is not null || spec.OrderByDescending is not null)
        {
            throw new InvalidOperationException(
                $"'{spec.GetType().Name}' declares an ordering, but '{method}' orders by the key selector and the "
                + "identity. Remove the ordering from the specification.");
        }
    }

    private static void EnsureNotSelfPaged<T>(ISpecification<T> spec, string method)
    {
        if (spec.Skip.HasValue || spec.Take.HasValue)
        {
            throw new InvalidOperationException(
                $"'{spec.GetType().Name}' declares Skip/Take, and '{method}' pages with the page request it is "
                + "given. Remove the paging from the specification.");
        }
    }
}

/// <summary>Presents a specification without its Skip/Take, for counting every match.</summary>
internal sealed class UnpagedSpecification<T>(ISpecification<T> inner) : ISpecification<T>
{
    public Expression<Func<T, bool>>? Criteria => inner.Criteria;

    public IReadOnlyList<Expression<Func<T, object>>> Includes => inner.Includes;

    public Expression<Func<T, object>>? OrderBy => inner.OrderBy;

    public Expression<Func<T, object>>? OrderByDescending => inner.OrderByDescending;

    public IReadOnlyList<(Expression<Func<T, object>> KeySelector, bool Descending)> ThenBys => inner.ThenBys;

    public int? Skip => null;

    public int? Take => null;

    public bool IsDistinct => inner.IsDistinct;

    public bool AsSplitQuery => inner.AsSplitQuery;

    public bool IncludeDeleted => inner.IncludeDeleted;

    public IReadOnlyList<string> StringIncludes => inner.StringIncludes;
}

namespace SharedKernel.Domain.Specifications;

/// <summary>
/// Abstract base class for paged, read-only query specifications.
/// Extends <see cref="ReadOnlySpecification{T}"/> — <c>AsNoTracking</c> is always
/// <see langword="true"/>.
/// </summary>
/// <typeparam name="T">The type of domain entity this specification applies to.</typeparam>
/// <remarks>
/// <para>
/// The constructor accepts a 1-based page number and a page size. Paging is applied
/// automatically: <c>Skip = (page - 1) * pageSize</c>, <c>Take = pageSize</c>.
/// </para>
/// <para>
/// Guards: <c>page &lt; 1</c>, <c>pageSize &lt; 1</c>, and <c>pageSize &gt; <see cref="MaxPageSize"/></c>
/// each throw <see cref="ArgumentOutOfRangeException"/>. Subclasses may shadow
/// <see cref="MaxPageSize"/> to apply a tighter limit.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public sealed class ActiveOrdersPagedSpec : PagedSpecification&lt;Order&gt;
/// {
///     public ActiveOrdersPagedSpec(int page, int pageSize) : base(page, pageSize)
///     {
///         AddCriteria(o => !o.IsDeleted);
///         ApplyOrderByDescending(o => o.CreatedOn);
///     }
/// }
/// </code>
/// </example>
public abstract class PagedSpecification<T> : ReadOnlySpecification<T>
{
    /// <summary>Maximum allowed page size. Subclasses may shadow this constant.</summary>
    protected const int MaxPageSize = 1000;

    /// <summary>
    /// Initialises a new paged specification for the specified page and page size.
    /// </summary>
    /// <param name="page">The 1-based page number (first page = 1).</param>
    /// <param name="pageSize">The number of items per page.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="page"/> is less than 1, <paramref name="pageSize"/> is less than 1,
    /// <paramref name="pageSize"/> exceeds <see cref="MaxPageSize"/>, or the page starts beyond
    /// <see cref="int.MaxValue"/> rows.
    /// </exception>
    protected PagedSpecification(int page, int pageSize)
    {
        if (page < 1)
            throw new ArgumentOutOfRangeException(nameof(page), page,
                "Page number must be greater than or equal to 1.");
        if (pageSize < 1)
            throw new ArgumentOutOfRangeException(nameof(pageSize), pageSize,
                "Page size must be greater than or equal to 1.");
        if (pageSize > MaxPageSize)
            throw new ArgumentOutOfRangeException(nameof(pageSize), pageSize,
                $"Page size must not exceed {MaxPageSize}.");

        var skip = (long)(page - 1) * pageSize;
        if (skip > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(page), page,
                $"Page {page} of size {pageSize} starts beyond the largest supported offset.");

        Page = page;
        PageSize = pageSize;
        ApplyPaging((int)skip, pageSize);
    }

    /// <summary>Gets the 1-based page number.</summary>
    public int Page { get; }

    /// <summary>Gets the number of items per page.</summary>
    public int PageSize { get; }
}

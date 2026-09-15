namespace SharedKernel.Domain.Specifications;

/// <summary>
/// Base class for a read-only query that returns one page of results, addressed by a 1-based page number and
/// a page size.
/// </summary>
/// <typeparam name="T">The entity type the query returns.</typeparam>
/// <remarks>
/// <para>
/// <b>Paging.</b> The constructor sets <see cref="Specification{T}.Skip"/> to <c>(page - 1) * pageSize</c>
/// and <see cref="Specification{T}.Take"/> to <c>pageSize</c>. The query always runs without change
/// tracking, inherited from <see cref="ReadOnlySpecification{T}"/>.
/// </para>
/// <para>
/// <b>Usage.</b> Always apply a primary sort in the subclass constructor, ending in a unique key such as the
/// identity key, so pages neither overlap nor skip rows. Do not call
/// <see cref="Specification{T}.ApplyPaging"/> again; it would replace the computed page.
/// </para>
/// <para>
/// <b>Pitfall.</b> Offset paging gets slower with depth and shifts when rows are inserted or deleted between
/// requests. For deep or live result sets use <see cref="KeysetSpecification{T, TKey}"/>.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public sealed class ActiveOrdersPageSpec : PagedSpecification&lt;Order&gt;
/// {
///     public ActiveOrdersPageSpec(int page, int pageSize) : base(page, pageSize)
///     {
///         AddCriteria(o =&gt; !o.IsDeleted);
///         ApplyOrderByDescending(o =&gt; o.CreatedOn);
///         ApplyThenBy(o =&gt; o.Id, descending: true);
///     }
/// }
/// </code>
/// </example>
public abstract class PagedSpecification<T> : ReadOnlySpecification<T>
{
    /// <summary>The largest page size the constructor accepts: 1000.</summary>
    /// <remarks>
    /// Shadowing this constant in a subclass does not change the check, which always uses this value. To
    /// enforce a smaller limit, validate the page size in the subclass constructor.
    /// </remarks>
    protected const int MaxPageSize = 1000;

    /// <summary>
    /// Initializes a new paged specification for the given 1-based page number and page size.
    /// </summary>
    /// <param name="page">The 1-based page number. Must be at least 1.</param>
    /// <param name="pageSize">
    /// The maximum number of rows per page. Must be between 1 and <see cref="MaxPageSize"/>.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="page"/> is less than 1; <paramref name="pageSize"/> is less than 1 or greater than
    /// <see cref="MaxPageSize"/>; or the page's first row offset, <c>(page - 1) * pageSize</c>, exceeds
    /// <see cref="int.MaxValue"/>.
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

    /// <summary>Gets the 1-based page number this specification returns.</summary>
    public int Page { get; }

    /// <summary>Gets the maximum number of rows per page, equal to <see cref="Specification{T}.Take"/>.</summary>
    public int PageSize { get; }
}

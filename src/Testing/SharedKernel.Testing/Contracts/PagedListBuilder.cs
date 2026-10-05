using SharedKernel.Contracts.Pagination;

namespace SharedKernel.Testing.Contracts;

/// <summary>
/// Fluent test builder eliminating repetitive <see cref="PagedList{T}"/> factory boilerplate in paged-query test
/// setups.
/// </summary>
/// <typeparam name="T">The item type contained in the paged list.</typeparam>
/// <remarks>
/// <see cref="Build"/> goes through <see cref="PagedList{T}.Create(IReadOnlyList{T}, int, int, long)"/>, so the
/// same rules apply as in production: the page and page size must be at least 1, the total count must not be
/// negative, and the page must not hold more items than its page size.
/// </remarks>
public sealed class PagedListBuilder<T>
{
    private IReadOnlyList<T> _items = [];
    private int _page = 1;
    private int _pageSize = 10;
    private long? _totalCount;

    /// <summary>
    /// Sets the items for this page. Also sets the default <see cref="WithTotalCount"/> value to
    /// <c>items.Count</c> unless overridden by a later call to <see cref="WithTotalCount"/>.
    /// </summary>
    /// <param name="items">The items to include on this page.</param>
    /// <returns>This builder, for fluent chaining.</returns>
    public PagedListBuilder<T> WithItems(IEnumerable<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        var materialized = items.ToList();
        _items = materialized;
        _totalCount ??= materialized.Count;
        return this;
    }

    /// <summary>Sets the 1-based page number. Defaults to <c>1</c>.</summary>
    /// <param name="page">The page number.</param>
    /// <returns>This builder, for fluent chaining.</returns>
    public PagedListBuilder<T> WithPage(int page)
    {
        _page = page;
        return this;
    }

    /// <summary>Sets the page size. Defaults to <c>10</c>.</summary>
    /// <param name="pageSize">The page size.</param>
    /// <returns>This builder, for fluent chaining.</returns>
    public PagedListBuilder<T> WithPageSize(int pageSize)
    {
        _pageSize = pageSize;
        return this;
    }

    /// <summary>Sets the page number and page size from a validated <see cref="PageRequest"/>.</summary>
    /// <param name="request">The request the page answers. Must not be null.</param>
    /// <returns>This builder, for fluent chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    public PagedListBuilder<T> WithRequest(PageRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        _page = request.Page;
        _pageSize = request.PageSize;
        return this;
    }

    /// <summary>
    /// Overrides the total record count. By default this equals the number of items supplied via
    /// <see cref="WithItems"/>.
    /// </summary>
    /// <param name="totalCount">The total record count across all pages.</param>
    /// <returns>This builder, for fluent chaining.</returns>
    public PagedListBuilder<T> WithTotalCount(long totalCount)
    {
        _totalCount = totalCount;
        return this;
    }

    /// <summary>Builds the configured <see cref="PagedList{T}"/>.</summary>
    /// <returns>A new <see cref="PagedList{T}"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The page or page size is below 1, or the total count is negative.</exception>
    /// <exception cref="ArgumentException">More items were supplied than the page size allows.</exception>
    public PagedList<T> Build() =>
        PagedList<T>.Create(_items, _page, _pageSize, _totalCount ?? _items.Count);

    /// <summary>Creates an empty <see cref="PagedList{T}"/> (zero items, page 1, page size 10).</summary>
    /// <returns>An empty <see cref="PagedList{T}"/>.</returns>
    public static PagedList<T> Empty() => PagedList<T>.Create([], page: 1, pageSize: 10, totalCount: 0);
}

using SharedKernel.Contracts.Pagination;

namespace SharedKernel.Testing.Contracts;

/// <summary>
/// Fluent test builder eliminating repetitive <see cref="PagedList{T}.Create"/> boilerplate in
/// paged-query test setups.
/// </summary>
/// <typeparam name="T">The item type contained in the paged list.</typeparam>
public sealed class PagedListBuilder<T>
{
    private IReadOnlyList<T> _items = [];
    private int _page = 1;
    private int _pageSize = 10;
    private int? _totalCount;

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

    /// <summary>
    /// Overrides the total record count. By default this equals the number of items supplied via
    /// <see cref="WithItems"/>.
    /// </summary>
    /// <param name="totalCount">The total record count across all pages.</param>
    /// <returns>This builder, for fluent chaining.</returns>
    public PagedListBuilder<T> WithTotalCount(int totalCount)
    {
        _totalCount = totalCount;
        return this;
    }

    /// <summary>Builds the configured <see cref="PagedList{T}"/>.</summary>
    /// <returns>A new <see cref="PagedList{T}"/>.</returns>
    public PagedList<T> Build() =>
        PagedList<T>.Create(_items, _page, _pageSize, _totalCount ?? _items.Count);

    /// <summary>Creates an empty <see cref="PagedList{T}"/> (zero items, page 1, page size 10).</summary>
    /// <returns>An empty <see cref="PagedList{T}"/>.</returns>
    public static PagedList<T> Empty() => PagedList<T>.Create([], page: 1, pageSize: 10, totalCount: 0);
}

namespace SharedKernel.Contracts.Pagination;

/// <summary>
/// A cross-service paged result DTO carrying a page of items alongside pagination metadata.
/// </summary>
/// <typeparam name="T">The item type contained in this page.</typeparam>
/// <remarks>
/// <para>
/// <see cref="Page"/> is 1-based — page 1 is the first page. This is consistent with
/// <c>PagedSpecification&lt;T&gt;</c> in <c>03.Domain</c>.
/// </para>
/// <para>
/// <see cref="Create"/> is the only permitted construction path. The primary record constructor
/// is <c>private init</c> to enforce this constraint.
/// </para>
/// <para>
/// <see cref="TotalPages"/> divides <see cref="TotalCount"/> by <see cref="PageSize"/> using
/// <c>Math.Ceiling</c>. When <see cref="TotalCount"/> is zero, <see cref="TotalPages"/> returns 0.
/// <see cref="PageSize"/> is guaranteed to be at least 1 by the <see cref="Create"/> guard, so
/// divide-by-zero is impossible.
/// </para>
/// </remarks>
public sealed record PagedList<T>
{
    /// <summary>Gets the items on the current page.</summary>
    public IReadOnlyList<T> Items { get; private init; }

    /// <summary>Gets the current page number (1-based; page 1 = first page).</summary>
    public int Page { get; private init; }

    /// <summary>Gets the maximum number of items per page.</summary>
    public int PageSize { get; private init; }

    /// <summary>Gets the total number of records across all pages.</summary>
    public int TotalCount { get; private init; }

    /// <summary>
    /// Gets the total number of pages required to display all records.
    /// Returns 0 when <see cref="TotalCount"/> is 0.
    /// </summary>
    public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);

    /// <summary>Gets a value indicating whether there is a page after the current one.</summary>
    public bool HasNextPage => Page < TotalPages;

    /// <summary>Gets a value indicating whether there is a page before the current one.</summary>
    public bool HasPreviousPage => Page > 1;

    [System.Text.Json.Serialization.JsonConstructor]
    internal PagedList(IReadOnlyList<T> items, int page, int pageSize, int totalCount)
    {
        Items = items;
        Page = page;
        PageSize = pageSize;
        TotalCount = totalCount;
    }

    /// <summary>
    /// Creates a new <see cref="PagedList{T}"/> with the specified page of items and metadata.
    /// </summary>
    /// <param name="items">The items on the current page. Must not be <c>null</c>.</param>
    /// <param name="page">The current page number. Must be at least 1.</param>
    /// <param name="pageSize">The maximum number of items per page. Must be at least 1.</param>
    /// <param name="totalCount">The total number of records across all pages. Must be non-negative.</param>
    /// <returns>A new <see cref="PagedList{T}"/> instance.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="items"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="page"/> is less than 1, <paramref name="pageSize"/> is less than 1,
    /// or <paramref name="totalCount"/> is negative.
    /// </exception>
    public static PagedList<T> Create(IReadOnlyList<T> items, int page, int pageSize, int totalCount)
    {
        ArgumentNullException.ThrowIfNull(items);

        if (page < 1)
            throw new ArgumentOutOfRangeException(nameof(page), page, "Page must be at least 1.");

        if (pageSize < 1)
            throw new ArgumentOutOfRangeException(nameof(pageSize), pageSize, "PageSize must be at least 1.");

        if (totalCount < 0)
            throw new ArgumentOutOfRangeException(nameof(totalCount), totalCount, "TotalCount must be non-negative.");

        return new PagedList<T>(items, page, pageSize, totalCount);
    }
}

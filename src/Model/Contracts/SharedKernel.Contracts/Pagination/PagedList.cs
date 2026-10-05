using System.Text.Json;
using System.Text.Json.Serialization;

namespace SharedKernel.Contracts.Pagination;

/// <summary>
/// One page of an offset-paged result: the items, the page's position, and the total number of items across all
/// pages.
/// </summary>
/// <typeparam name="T">The item type.</typeparam>
/// <remarks>
/// <para>
/// <b>Construction.</b> Create one with <see cref="Create(IReadOnlyList{T}, PageRequest, long)"/> or
/// <see cref="Create(IReadOnlyList{T}, int, int, long)"/>. The items are copied, so changing the source list later
/// does not change the page.
/// </para>
/// <para>
/// <b>Consistency.</b> The page and the total usually come from two queries. Rows written between them can make
/// <see cref="TotalCount"/> disagree slightly with the items, so the only item-count rule enforced is that a page
/// never holds more than <see cref="PageSize"/> items. A page past the end is valid and empty.
/// </para>
/// <para>
/// <b>Equality.</b> Two pages are equal when their positions, totals and items are equal, item by item.
/// </para>
/// <para>
/// <b>Wire shape.</b> JSON member names are fixed (<c>items</c>, <c>page</c>, <c>pageSize</c>, <c>totalCount</c>,
/// <c>totalPages</c>, <c>hasNextPage</c>, <c>hasPreviousPage</c>) and do not depend on the serializer's naming
/// policy. The last three are computed; they are written but ignored when reading. Reading a document that breaks
/// a rule throws <see cref="JsonException"/>.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// var request = PageRequest.Create(page: 2, pageSize: 50).Value;
/// PagedList&lt;Order&gt; orders = PagedList&lt;Order&gt;.Create(rows, request, totalCount);
/// PagedList&lt;OrderSummary&gt; response = orders.Map(o =&gt; new OrderSummary(o.Id.Value, o.Status.Name));
/// </code>
/// </example>
public sealed record PagedList<T>
{
    [JsonConstructor]
    internal PagedList(IReadOnlyList<T> items, int page, int pageSize, long totalCount)
    {
        if (FindProblem(items, page, pageSize, totalCount) is { } problem)
            throw new JsonException($"Invalid paged list: {problem.Message}");

        Items = PageItems.Snapshot(items);
        Page = page;
        PageSize = pageSize;
        TotalCount = totalCount;
    }

    private PagedList(IReadOnlyList<T> snapshot, int page, int pageSize, long totalCount, bool _)
    {
        Items = snapshot;
        Page = page;
        PageSize = pageSize;
        TotalCount = totalCount;
    }

    /// <summary>Gets the items on this page, at most <see cref="PageSize"/>, in query order.</summary>
    [JsonPropertyName("items")]
    public IReadOnlyList<T> Items { get; }

    /// <summary>Gets the 1-based page number.</summary>
    [JsonPropertyName("page")]
    public int Page { get; }

    /// <summary>Gets the maximum number of items per page, at least 1.</summary>
    [JsonPropertyName("pageSize")]
    public int PageSize { get; }

    /// <summary>Gets the total number of items across all pages, at least 0.</summary>
    [JsonPropertyName("totalCount")]
    public long TotalCount { get; }

    /// <summary>Gets the number of pages needed for <see cref="TotalCount"/> items; 0 when there are none.</summary>
    [JsonPropertyName("totalPages")]
    public long TotalPages => TotalCount == 0 ? 0 : ((TotalCount - 1) / PageSize) + 1;

    /// <summary>Gets a value indicating whether a page exists after this one.</summary>
    [JsonPropertyName("hasNextPage")]
    public bool HasNextPage => Page < TotalPages;

    /// <summary>Gets a value indicating whether a page exists before this one, which is true for every page after the first.</summary>
    [JsonPropertyName("hasPreviousPage")]
    public bool HasPreviousPage => Page > 1;

    /// <summary>Creates a page for a validated request.</summary>
    /// <param name="items">The items on the page. Must not be null or hold more than the request's page size.</param>
    /// <param name="request">The request the page answers. Must not be null.</param>
    /// <param name="totalCount">The total number of items across all pages. Must not be negative.</param>
    /// <returns>The page.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="items"/> or <paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="totalCount"/> is negative.</exception>
    /// <exception cref="ArgumentException"><paramref name="items"/> holds more items than the page size.</exception>
    public static PagedList<T> Create(IReadOnlyList<T> items, PageRequest request, long totalCount)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Create(items, request.Page, request.PageSize, totalCount);
    }

    /// <summary>Creates a page from a page number and page size.</summary>
    /// <param name="items">The items on the page. Must not be null or hold more than <paramref name="pageSize"/> items.</param>
    /// <param name="page">The 1-based page number. Must be at least 1.</param>
    /// <param name="pageSize">The maximum number of items per page. Must be at least 1.</param>
    /// <param name="totalCount">The total number of items across all pages. Must not be negative.</param>
    /// <returns>The page.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="items"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="page"/> or <paramref name="pageSize"/> is below 1, or <paramref name="totalCount"/> is negative.
    /// </exception>
    /// <exception cref="ArgumentException"><paramref name="items"/> holds more than <paramref name="pageSize"/> items.</exception>
    public static PagedList<T> Create(IReadOnlyList<T> items, int page, int pageSize, long totalCount)
    {
        if (FindProblem(items, page, pageSize, totalCount) is { } problem)
        {
            throw problem.Parameter switch
            {
                nameof(items) when items is null => new ArgumentNullException(nameof(items)),
                nameof(items) => new ArgumentException(problem.Message, nameof(items)),
                _ => new ArgumentOutOfRangeException(problem.Parameter, problem.Message),
            };
        }

        return new PagedList<T>(PageItems.Snapshot(items), page, pageSize, totalCount, false);
    }

    /// <summary>Creates an empty page for a request, with a total of zero.</summary>
    /// <param name="request">The request the page answers. Must not be null.</param>
    /// <returns>An empty page at the request's position.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    public static PagedList<T> Empty(PageRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new PagedList<T>([], request.Page, request.PageSize, 0, false);
    }

    /// <summary>Projects every item into a new page with the same position and total.</summary>
    /// <typeparam name="TResult">The projected item type.</typeparam>
    /// <param name="selector">The projection, called once per item in order. Must not be null.</param>
    /// <returns>A new page of projected items.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="selector"/> is <see langword="null"/>.</exception>
    public PagedList<TResult> Map<TResult>(Func<T, TResult> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        return new PagedList<TResult>(PageItems.Map(Items, selector), Page, PageSize, TotalCount, false);
    }

    /// <summary>Returns whether <paramref name="other"/> has the same position, total and items.</summary>
    /// <param name="other">The page to compare with.</param>
    /// <returns><see langword="true"/> when every member and every item is equal; otherwise <see langword="false"/>.</returns>
    public bool Equals(PagedList<T>? other) =>
        other is not null
        && Page == other.Page
        && PageSize == other.PageSize
        && TotalCount == other.TotalCount
        && PageItems.SequenceEqual(Items, other.Items);

    /// <summary>Returns a hash code consistent with <see cref="Equals(PagedList{T})"/>.</summary>
    /// <returns>A hash of the position, the total and every item.</returns>
    public override int GetHashCode() =>
        HashCode.Combine(Page, PageSize, TotalCount, PageItems.SequenceHash(Items));

    private static (string Parameter, string Message)? FindProblem(IReadOnlyList<T>? items, int page, int pageSize, long totalCount)
    {
        if (items is null)
            return ("items", "Items must not be null.");

        if (page < 1)
            return ("page", $"Page must be at least 1 but was {page}.");

        if (pageSize < 1)
            return ("pageSize", $"Page size must be at least 1 but was {pageSize}.");

        if (totalCount < 0)
            return ("totalCount", $"Total count must not be negative but was {totalCount}.");

        if (items.Count > pageSize)
            return ("items", $"A page of size {pageSize} cannot hold {items.Count} items.");

        return null;
    }
}

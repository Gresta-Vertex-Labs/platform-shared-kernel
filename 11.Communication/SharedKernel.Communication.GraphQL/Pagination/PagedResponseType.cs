using HotChocolate.Types.Pagination;

namespace SharedKernel.Communication.GraphQL.Pagination;

/// <summary>
/// Consistent pagination response wrapper for GraphQL queries.
/// Presents <see cref="TotalCount"/> and <see cref="Items"/> matching the shape of
/// <c>PagedList&lt;T&gt;</c> from <c>04.Contracts</c> for API shape consistency.
/// </summary>
/// <remarks>
/// Use <c>FromPage</c> to create an instance from a HotChocolate <see cref="IPage"/>
/// result (offset paging) or <c>FromConnection</c> from a cursor-paged
/// <see cref="Connection{T}"/>.
/// </remarks>
/// <typeparam name="T">The item type returned in the paged response.</typeparam>
public sealed class PagedResponseType<T>
{
    /// <summary>Total number of items matching the query (before paging).</summary>
    public int TotalCount { get; init; }

    /// <summary>Items in the current page.</summary>
    public IReadOnlyList<T> Items { get; init; } = [];

    /// <summary>
    /// Creates a <see cref="PagedResponseType{T}"/> from a HotChocolate offset-paged
    /// <see cref="IPage"/> result. Casts items from <c>object</c> to <typeparamref name="T"/>.
    /// </summary>
    /// <param name="page">The HotChocolate offset paging result.</param>
    /// <returns>A <see cref="PagedResponseType{T}"/> with <see cref="TotalCount"/> and <see cref="Items"/> populated.</returns>
    public static PagedResponseType<T> FromPage(IPage page)
    {
        ArgumentNullException.ThrowIfNull(page);

        var totalCount = page is IPageTotalCountProvider provider ? provider.TotalCount : 0;
        var items = page.Items.OfType<T>().ToList();

        return new PagedResponseType<T>
        {
            TotalCount = totalCount,
            Items = items,
        };
    }

    /// <summary>
    /// Creates a <see cref="PagedResponseType{T}"/> from a HotChocolate cursor-paged
    /// <see cref="Connection{T}"/> result.
    /// </summary>
    /// <param name="connection">The HotChocolate cursor paging connection.</param>
    /// <returns>A <see cref="PagedResponseType{T}"/> with <see cref="TotalCount"/> and <see cref="Items"/> populated.</returns>
    public static PagedResponseType<T> FromConnection(Connection<T> connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        var items = connection.Edges
            .Select(e => e.Node)
            .ToList();

        return new PagedResponseType<T>
        {
            TotalCount = connection.TotalCount,
            Items = items,
        };
    }

    /// <summary>
    /// Creates a <see cref="PagedResponseType{T}"/> directly from a list and total count.
    /// </summary>
    /// <param name="items">The items in the current page.</param>
    /// <param name="totalCount">The total number of items matching the query.</param>
    /// <returns>A new <see cref="PagedResponseType{T}"/>.</returns>
    public static PagedResponseType<T> From(IReadOnlyList<T> items, int totalCount) =>
        new() { Items = items, TotalCount = totalCount };
}

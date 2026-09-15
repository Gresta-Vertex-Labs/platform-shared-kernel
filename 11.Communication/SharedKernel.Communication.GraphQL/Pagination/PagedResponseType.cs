using HotChocolate.Types.Pagination;
using SharedKernel.Contracts.Pagination;

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
    /// <remarks>
    /// A <see cref="long"/>, matching <see cref="PagedList{T}.TotalCount"/>, so a repository total never
    /// narrows on its way to the client. HotChocolate exposes it as the <c>Long</c> scalar; totals from
    /// HotChocolate's own <see cref="IPage"/> and <see cref="Connection{T}"/> results are <see cref="int"/>
    /// and widen without loss.
    /// </remarks>
    public long TotalCount { get; init; }

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
    public static PagedResponseType<T> From(IReadOnlyList<T> items, long totalCount) =>
        new() { Items = items, TotalCount = totalCount };

    /// <summary>
    /// Creates a <see cref="PagedResponseType{T}"/> from a <see cref="PagedList{T}"/> produced by
    /// the application layer (e.g., from a repository projection).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Use this factory when your GraphQL resolver receives a <see cref="PagedList{T}"/> from
    /// the application or persistence layer. It maps <see cref="PagedList{T}.Items"/> and
    /// <see cref="PagedList{T}.TotalCount"/> directly to the corresponding properties on
    /// <see cref="PagedResponseType{T}"/>, preserving API shape parity with REST responses.
    /// </para>
    /// <para>
    /// Use <see cref="FromPage"/> when the data source is a HotChocolate offset-paged
    /// <see cref="IPage"/> result; use <see cref="FromConnection"/> for a cursor-paged
    /// <see cref="Connection{T}"/> result; use <see cref="From"/> when assembling the shape
    /// manually from a raw list and count.
    /// </para>
    /// <para>
    /// This factory depends on the <c>SharedKernel.Contracts</c> (<c>04.Contracts</c>) project
    /// reference in this package — required solely for this bridge method.
    /// </para>
    /// </remarks>
    /// <param name="pagedList">
    /// The <see cref="PagedList{T}"/> from the application layer. Must not be <c>null</c>.
    /// </param>
    /// <returns>
    /// A <see cref="PagedResponseType{T}"/> with <see cref="Items"/> and <see cref="TotalCount"/>
    /// populated from the source <see cref="PagedList{T}"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="pagedList"/> is <c>null</c>.
    /// </exception>
    public static PagedResponseType<T> FromPagedList(PagedList<T> pagedList)
    {
        ArgumentNullException.ThrowIfNull(pagedList);

        return new PagedResponseType<T>
        {
            Items = pagedList.Items,
            TotalCount = pagedList.TotalCount,
        };
    }
}

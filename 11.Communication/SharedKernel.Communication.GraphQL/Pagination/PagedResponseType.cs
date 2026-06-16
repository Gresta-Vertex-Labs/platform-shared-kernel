namespace SharedKernel.Communication.GraphQL.Pagination;

/// <summary>
/// Consistent pagination response wrapper for GraphQL queries.
/// Presents <see cref="TotalCount"/> and <see cref="Items"/> matching the shape of
/// <c>PagedList&lt;T&gt;</c> from <c>04.Contracts</c> for API shape consistency.
/// Wraps both <c>CollectionSegment&lt;T&gt;</c> (offset paging) and
/// <c>Connection&lt;T&gt;</c> (cursor paging) as source types.
/// </summary>
/// <typeparam name="T">The item type returned in the paged response.</typeparam>
public sealed class PagedResponseType<T>
{
    /// <summary>Total number of items matching the query (before paging).</summary>
    public int TotalCount { get; init; }

    /// <summary>Items in the current page.</summary>
    public IReadOnlyList<T> Items { get; init; } = [];
}

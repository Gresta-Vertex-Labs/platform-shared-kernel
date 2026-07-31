namespace SharedKernel.Contracts.Pagination;

/// <summary>
/// A cross-service cursor/keyset-paginated result DTO carrying a page of items alongside an opaque
/// forward cursor. The counterpart to <see cref="PagedList{T}"/> for offset-based pagination.
/// </summary>
/// <typeparam name="T">The item type contained in this page.</typeparam>
/// <remarks>
/// <para>
/// (WO-052/P-332) Deliberately carries no <c>TotalCount</c>, <c>Page</c>, or <c>PageSize</c> —
/// keyset pagination structurally cannot support random-access page numbers or a reliable total
/// count without defeating its own performance purpose. The reason to reach for keyset pagination
/// is to avoid the <c>COUNT(*)</c>/<c>OFFSET</c> cost that grows with table size; re-introducing
/// those members here would reintroduce that cost.
/// </para>
/// <para>
/// Use <see cref="PagedList{T}"/> for small/random-access/UI-paged result sets that need a total
/// count. Use <see cref="CursorPagedList{T}"/> for large, actively-written, or infinite-scroll
/// result sets where a stable total count is expensive or meaningless.
/// </para>
/// <para>
/// Doc-only cross-reference (no compile dependency): the specification-side counterpart is
/// <c>03.Domain</c>'s <c>KeysetSpecification&lt;T, TKey&gt;</c> (shipped
/// <c>SharedKernel.Domain</c> v1.7.0, P-308/WO-051). <c>06.Persistence</c>'s EF Core translation of
/// that specification into a real seek query remains design-locked and queued (P-317/WO-051) — this
/// DTO ships ahead of that translation, mirroring the platform's accepted "design-ahead-of-Core"
/// pattern.
/// </para>
/// <para>
/// <see cref="Create"/> is the only permitted construction path. The primary record constructor is
/// <c>internal</c> and annotated <c>[JsonConstructor]</c> for STJ source-generated deserialization
/// only — not a valid external construction path, mirroring <see cref="PagedList{T}"/>'s identical
/// pattern.
/// </para>
/// </remarks>
/// <seealso cref="PagedList{T}"/>
public sealed record CursorPagedList<T>
{
    /// <summary>Gets the items on the current page.</summary>
    public required IReadOnlyList<T> Items { get; init; }

    /// <summary>
    /// Gets the opaque forward cursor pointing past the last item in <see cref="Items"/>.
    /// <c>null</c> when there is no further page.
    /// </summary>
    /// <remarks>
    /// The cursor is opaque to consumers — it must be passed back verbatim to fetch the next page
    /// and must never be parsed, decoded, or constructed by a consumer.
    /// </remarks>
    public required string? NextCursor { get; init; }

    /// <summary>
    /// Gets a value indicating whether at least one more page is available beyond
    /// <see cref="NextCursor"/>.
    /// </summary>
    public required bool HasMore { get; init; }

    [System.Text.Json.Serialization.JsonConstructor]
    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    internal CursorPagedList(IReadOnlyList<T> items, string? nextCursor, bool hasMore)
    {
        Items = items;
        NextCursor = nextCursor;
        HasMore = hasMore;
    }

    /// <summary>
    /// Creates a new <see cref="CursorPagedList{T}"/> with the specified page of items and cursor
    /// metadata.
    /// </summary>
    /// <param name="items">The items on the current page. Must not be <c>null</c>.</param>
    /// <param name="nextCursor">
    /// The opaque forward cursor for the next page, or <c>null</c> when there is no further page.
    /// </param>
    /// <param name="hasMore">Whether at least one more page is available beyond <paramref name="nextCursor"/>.</param>
    /// <returns>A new <see cref="CursorPagedList{T}"/> instance.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="items"/> is <c>null</c>.</exception>
    public static CursorPagedList<T> Create(IReadOnlyList<T> items, string? nextCursor, bool hasMore)
    {
        ArgumentNullException.ThrowIfNull(items);

        return new CursorPagedList<T>(items, nextCursor, hasMore);
    }
}

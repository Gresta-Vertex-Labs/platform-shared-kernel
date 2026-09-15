using System.Text.Json;
using System.Text.Json.Serialization;

namespace SharedKernel.Contracts.Pagination;

/// <summary>
/// One page of a cursor-paged result: the items and the opaque cursor for the next page, with no page numbers or
/// total count.
/// </summary>
/// <typeparam name="T">The item type.</typeparam>
/// <remarks>
/// <para>
/// <b>Why no total.</b> Cursor paging exists to avoid the <c>COUNT</c> and <c>OFFSET</c> costs that grow with table
/// size. A total count or page numbers would bring those costs back. Use <see cref="PagedList{T}"/> when a client
/// needs them.
/// </para>
/// <para>
/// <b>Construction.</b> The simplest path is <see cref="FromLookahead"/>: fetch one item more than the limit, and it
/// works out whether another page exists and builds the cursor from the last item kept. Items are copied, so
/// changing the source list later does not change the page.
/// </para>
/// <para>
/// <b>End of results.</b> <see cref="NextCursor"/> is <see langword="null"/> exactly when there is no next page, and
/// <see cref="HasMore"/> is derived from it, so the two can never disagree.
/// </para>
/// <para>
/// <b>Wire shape.</b> JSON member names are fixed (<c>items</c>, <c>nextCursor</c>, <c>hasMore</c>). <c>hasMore</c>
/// is computed; it is written but ignored when reading. Reading a document that breaks a rule throws
/// <see cref="JsonException"/>.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // Query with Take = request.Limit + 1, ordered by (CreatedOn, Id).
/// IReadOnlyList&lt;Order&gt; rows = await repository.ListAsync(new RecentOrdersSpec(afterCreatedOn, afterId, request.Limit + 1), ct);
///
/// CursorPagedList&lt;OrderSummary&gt; page = CursorPagedList&lt;Order&gt;
///     .FromLookahead(rows, request.Limit, last =&gt; PageCursor.Encode(last.CreatedOn, last.Id.Value))
///     .Map(o =&gt; new OrderSummary(o.Id.Value, o.Status.Name));
/// </code>
/// </example>
public sealed record CursorPagedList<T>
{
    [JsonConstructor]
    internal CursorPagedList(IReadOnlyList<T> items, string? nextCursor)
    {
        if (FindProblem(items, nextCursor) is { } problem)
            throw new JsonException($"Invalid cursor paged list: {problem.Message}");

        Items = PageItems.Snapshot(items);
        NextCursor = nextCursor;
    }

    private CursorPagedList(IReadOnlyList<T> snapshot, string? nextCursor, bool _)
    {
        Items = snapshot;
        NextCursor = nextCursor;
    }

    /// <summary>Gets the items on this page, in query order.</summary>
    [JsonPropertyName("items")]
    public IReadOnlyList<T> Items { get; }

    /// <summary>
    /// Gets the opaque cursor to pass back for the next page, or <see langword="null"/> when this is the last page.
    /// </summary>
    /// <remarks>Clients pass it back verbatim and never parse or build one.</remarks>
    [JsonPropertyName("nextCursor")]
    public string? NextCursor { get; }

    /// <summary>Gets a value indicating whether a next page exists; the same as <see cref="NextCursor"/> being set.</summary>
    [JsonPropertyName("hasMore")]
    public bool HasMore => NextCursor is not null;

    /// <summary>Creates a page from its items and the cursor for the next page.</summary>
    /// <param name="items">The items on the page. Must not be null.</param>
    /// <param name="nextCursor">
    /// The cursor for the next page, or <see langword="null"/> for the last page. When set, must not be blank and
    /// must be at most <see cref="PageCursor.MaxLength"/> characters.
    /// </param>
    /// <returns>The page.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="items"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="nextCursor"/> is blank or too long.</exception>
    public static CursorPagedList<T> Create(IReadOnlyList<T> items, string? nextCursor)
    {
        ArgumentNullException.ThrowIfNull(items);

        if (FindProblem(items, nextCursor) is { } problem)
            throw new ArgumentException(problem.Message, nameof(nextCursor));

        return new CursorPagedList<T>(PageItems.Snapshot(items), nextCursor, false);
    }

    /// <summary>
    /// Creates a page from a query that fetched up to <paramref name="limit"/> + 1 items, keeping at most
    /// <paramref name="limit"/> and setting the next cursor only when the extra item proves another page exists.
    /// </summary>
    /// <param name="fetched">The items the query returned, at most <paramref name="limit"/> + 1. Must not be null.</param>
    /// <param name="limit">The page size the client asked for. Must be at least 1.</param>
    /// <param name="cursorFor">
    /// Builds the cursor from the last item kept, typically with <see cref="PageCursor.Encode{TKey, TId}"/>. Called
    /// at most once. Must not be null.
    /// </param>
    /// <returns>The page.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="fetched"/> or <paramref name="cursorFor"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="limit"/> is below 1.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="fetched"/> holds more than <paramref name="limit"/> + 1 items, or <paramref name="cursorFor"/>
    /// returns a blank or too long cursor.
    /// </exception>
    public static CursorPagedList<T> FromLookahead(IReadOnlyList<T> fetched, int limit, Func<T, string> cursorFor)
    {
        ArgumentNullException.ThrowIfNull(fetched);
        ArgumentNullException.ThrowIfNull(cursorFor);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);

        if (fetched.Count > (long)limit + 1)
        {
            throw new ArgumentException(
                $"Fetch at most limit + 1 ({(long)limit + 1}) items; {fetched.Count} were supplied.",
                nameof(fetched));
        }

        if (fetched.Count <= limit)
            return new CursorPagedList<T>(PageItems.Snapshot(fetched), null, false);

        var kept = new T[limit];
        for (var i = 0; i < limit; i++)
            kept[i] = fetched[i];

        var nextCursor = cursorFor(kept[^1]);
        if (FindProblem(kept, nextCursor) is { } problem)
            throw new ArgumentException(problem.Message, nameof(cursorFor));

        return new CursorPagedList<T>(Array.AsReadOnly(kept), nextCursor, false);
    }

    /// <summary>Creates an empty last page.</summary>
    /// <returns>A page with no items and no next cursor.</returns>
    public static CursorPagedList<T> Empty() => new([], null, false);

    /// <summary>Projects every item into a new page with the same next cursor.</summary>
    /// <typeparam name="TResult">The projected item type.</typeparam>
    /// <param name="selector">The projection, called once per item in order. Must not be null.</param>
    /// <returns>A new page of projected items.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="selector"/> is <see langword="null"/>.</exception>
    public CursorPagedList<TResult> Map<TResult>(Func<T, TResult> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        return new CursorPagedList<TResult>(PageItems.Map(Items, selector), NextCursor, false);
    }

    /// <summary>Returns whether <paramref name="other"/> has the same next cursor and items.</summary>
    /// <param name="other">The page to compare with.</param>
    /// <returns><see langword="true"/> when the cursor and every item are equal; otherwise <see langword="false"/>.</returns>
    public bool Equals(CursorPagedList<T>? other) =>
        other is not null
        && string.Equals(NextCursor, other.NextCursor, StringComparison.Ordinal)
        && PageItems.SequenceEqual(Items, other.Items);

    /// <summary>Returns a hash code consistent with <see cref="Equals(CursorPagedList{T})"/>.</summary>
    /// <returns>A hash of the cursor and every item.</returns>
    public override int GetHashCode() =>
        HashCode.Combine(NextCursor is null ? 0 : StringComparer.Ordinal.GetHashCode(NextCursor), PageItems.SequenceHash(Items));

    private static (string Parameter, string Message)? FindProblem(IReadOnlyList<T>? items, string? nextCursor)
    {
        if (items is null)
            return ("items", "Items must not be null.");

        if (nextCursor is not null && (string.IsNullOrWhiteSpace(nextCursor) || nextCursor.Length > PageCursor.MaxLength))
            return ("nextCursor", $"The next cursor must be null, or not blank and at most {PageCursor.MaxLength} characters.");

        return null;
    }
}

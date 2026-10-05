using SharedKernel.Contracts.Pagination;

namespace SharedKernel.Testing.Contracts;

/// <summary>
/// Plain-exception assertion helpers over <see cref="PagedList{T}"/> and <see cref="CursorPagedList{T}"/>.
/// </summary>
/// <remarks>
/// Zero dependency on FluentAssertions or any other test-framework assertion library — this
/// package's standing hard rule (no assertion-library dependency of its own) takes precedence.
/// </remarks>
public static class PagedListAssertions
{
    /// <summary>Asserts that <paramref name="list"/>'s <see cref="PagedList{T}.TotalCount"/> equals <paramref name="expected"/>.</summary>
    /// <typeparam name="T">The item type contained in the paged list.</typeparam>
    /// <param name="list">The paged list to evaluate.</param>
    /// <param name="expected">The expected total count.</param>
    /// <exception cref="InvalidOperationException">The actual total count does not match.</exception>
    public static void ShouldHaveTotalCount<T>(this PagedList<T> list, long expected)
    {
        ArgumentNullException.ThrowIfNull(list);

        if (list.TotalCount != expected)
        {
            throw new InvalidOperationException(
                $"Expected TotalCount {expected} but found {list.TotalCount}.");
        }
    }

    /// <summary>
    /// Asserts that <paramref name="list"/>'s items equal <paramref name="expected"/>, in order.
    /// </summary>
    /// <typeparam name="T">The item type contained in the paged list.</typeparam>
    /// <param name="list">The paged list to evaluate.</param>
    /// <param name="expected">The expected items, in order.</param>
    /// <exception cref="InvalidOperationException">The items do not match.</exception>
    public static void ShouldHaveItems<T>(this PagedList<T> list, params T[] expected)
    {
        ArgumentNullException.ThrowIfNull(list);
        AssertItems(list.Items, expected);
    }

    /// <summary>Asserts that <paramref name="list"/> contains no items.</summary>
    /// <typeparam name="T">The item type contained in the paged list.</typeparam>
    /// <param name="list">The paged list to evaluate.</param>
    /// <exception cref="InvalidOperationException"><paramref name="list"/> is non-empty.</exception>
    public static void ShouldBeEmpty<T>(this PagedList<T> list)
    {
        ArgumentNullException.ThrowIfNull(list);
        AssertEmpty(list.Items);
    }

    /// <summary>
    /// Asserts that <paramref name="list"/>'s items equal <paramref name="expected"/>, in order.
    /// </summary>
    /// <typeparam name="T">The item type contained in the cursor-paged list.</typeparam>
    /// <param name="list">The cursor-paged list to evaluate.</param>
    /// <param name="expected">The expected items, in order.</param>
    /// <exception cref="InvalidOperationException">The items do not match.</exception>
    public static void ShouldHaveItems<T>(this CursorPagedList<T> list, params T[] expected)
    {
        ArgumentNullException.ThrowIfNull(list);
        AssertItems(list.Items, expected);
    }

    /// <summary>Asserts that <paramref name="list"/> contains no items.</summary>
    /// <typeparam name="T">The item type contained in the cursor-paged list.</typeparam>
    /// <param name="list">The cursor-paged list to evaluate.</param>
    /// <exception cref="InvalidOperationException"><paramref name="list"/> is non-empty.</exception>
    public static void ShouldBeEmpty<T>(this CursorPagedList<T> list)
    {
        ArgumentNullException.ThrowIfNull(list);
        AssertEmpty(list.Items);
    }

    /// <summary>
    /// Asserts that <paramref name="list"/> has a next page, and returns its <see cref="CursorPagedList{T}.NextCursor"/>.
    /// </summary>
    /// <typeparam name="T">The item type contained in the cursor-paged list.</typeparam>
    /// <param name="list">The cursor-paged list to evaluate.</param>
    /// <returns>The cursor for the next page.</returns>
    /// <exception cref="InvalidOperationException"><paramref name="list"/> is the last page.</exception>
    public static string ShouldHaveNextPage<T>(this CursorPagedList<T> list)
    {
        ArgumentNullException.ThrowIfNull(list);

        return list.NextCursor
            ?? throw new InvalidOperationException("Expected a next page cursor, but the list is the last page.");
    }

    /// <summary>Asserts that <paramref name="list"/> is the last page, with no next cursor.</summary>
    /// <typeparam name="T">The item type contained in the cursor-paged list.</typeparam>
    /// <param name="list">The cursor-paged list to evaluate.</param>
    /// <exception cref="InvalidOperationException"><paramref name="list"/> has a next page.</exception>
    public static void ShouldBeLastPage<T>(this CursorPagedList<T> list)
    {
        ArgumentNullException.ThrowIfNull(list);

        if (list.NextCursor is not null)
        {
            throw new InvalidOperationException(
                $"Expected the last page, but found next cursor '{list.NextCursor}'.");
        }
    }

    private static void AssertItems<T>(IReadOnlyList<T> actual, T[] expected)
    {
        ArgumentNullException.ThrowIfNull(expected);

        if (!actual.SequenceEqual(expected))
        {
            throw new InvalidOperationException(
                $"Expected items [{string.Join(", ", expected)}] but found [{string.Join(", ", actual)}].");
        }
    }

    private static void AssertEmpty<T>(IReadOnlyList<T> actual)
    {
        if (actual.Count > 0)
        {
            throw new InvalidOperationException(
                $"Expected an empty paged list but found {actual.Count} item(s).");
        }
    }
}

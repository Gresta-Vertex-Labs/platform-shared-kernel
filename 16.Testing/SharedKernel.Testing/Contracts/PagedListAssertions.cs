using SharedKernel.Contracts.Pagination;

namespace SharedKernel.Testing.Contracts;

/// <summary>
/// Plain-exception assertion helpers over <see cref="PagedList{T}"/>.
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
    public static void ShouldHaveTotalCount<T>(this PagedList<T> list, int expected)
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
        ArgumentNullException.ThrowIfNull(expected);

        if (!list.Items.SequenceEqual(expected))
        {
            throw new InvalidOperationException(
                $"Expected items [{string.Join(", ", expected)}] but found [{string.Join(", ", list.Items)}].");
        }
    }

    /// <summary>Asserts that <paramref name="list"/> contains no items.</summary>
    /// <typeparam name="T">The item type contained in the paged list.</typeparam>
    /// <param name="list">The paged list to evaluate.</param>
    /// <exception cref="InvalidOperationException"><paramref name="list"/> is non-empty.</exception>
    public static void ShouldBeEmpty<T>(this PagedList<T> list)
    {
        ArgumentNullException.ThrowIfNull(list);

        if (list.Items.Count > 0)
        {
            throw new InvalidOperationException(
                $"Expected an empty paged list but found {list.Items.Count} item(s).");
        }
    }
}

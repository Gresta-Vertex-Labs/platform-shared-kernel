namespace SharedKernel.Core.Extensions;

/// <summary>
/// Extension methods for <see cref="IEnumerable{T}"/>.
/// </summary>
public static class EnumerableExtensions
{
    /// <summary>
    /// Splits <paramref name="source"/> into consecutive batches of at most
    /// <paramref name="size"/> elements.
    /// </summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="source">The sequence to batch. Must not be <c>null</c>.</param>
    /// <param name="size">The maximum number of elements in each batch. Must be greater than zero.</param>
    /// <returns>
    /// A sequence of arrays, each containing at most <paramref name="size"/> elements.
    /// The last batch may contain fewer elements if the source is not evenly divisible.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="source"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="size"/> is less than 1.</exception>
    public static IEnumerable<T[]> ToBatches<T>(this IEnumerable<T> source, int size)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentOutOfRangeException.ThrowIfLessThan(size, 1);

        var batch = new List<T>(size);

        foreach (var item in source)
        {
            batch.Add(item);
            if (batch.Count == size)
            {
                yield return batch.ToArray();
                batch.Clear();
            }
        }

        if (batch.Count > 0)
            yield return batch.ToArray();
    }

    /// <summary>
    /// Returns <c>true</c> if <paramref name="source"/> is <c>null</c> or contains no elements.
    /// </summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="source">The sequence to test.</param>
    public static bool IsNullOrEmpty<T>(this IEnumerable<T>? source)
        => source is null || !source.Any();

    /// <summary>
    /// Filters out <c>null</c> elements from <paramref name="source"/> and returns only
    /// non-null values.
    /// </summary>
    /// <typeparam name="T">The element type (reference type).</typeparam>
    /// <param name="source">The sequence to filter. Must not be <c>null</c>.</param>
    /// <returns>A sequence containing only non-null elements.</returns>
    public static IEnumerable<T> WhereNotNull<T>(this IEnumerable<T?> source)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(source);
        return source.Where(item => item is not null)!;
    }
}

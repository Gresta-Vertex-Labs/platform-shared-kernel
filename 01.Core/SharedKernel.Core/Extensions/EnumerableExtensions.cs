using System.Diagnostics.CodeAnalysis;

namespace SharedKernel.Core.Extensions;

/// <summary>
/// Extension methods for <see cref="IEnumerable{T}"/>.
/// </summary>
/// <remarks>
/// To split a sequence into fixed-size batches, use <see cref="Enumerable.Chunk{TSource}(IEnumerable{TSource}, int)"/>.
/// </remarks>
public static class EnumerableExtensions
{
    /// <summary>
    /// Returns <see langword="true"/> if <paramref name="source"/> is <see langword="null"/> or contains no
    /// elements.
    /// </summary>
    /// <remarks>Uses the collection's count when it has one; otherwise reads at most one element.</remarks>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="source">The sequence to test.</param>
    public static bool IsNullOrEmpty<T>([NotNullWhen(false)] this IEnumerable<T>? source)
    {
        if (source is null)
            return true;

        return source.TryGetNonEnumeratedCount(out var count) ? count == 0 : !source.Any();
    }

    /// <summary>
    /// Returns the elements of <paramref name="source"/> that are not <see langword="null"/>.
    /// </summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="source">The sequence to filter.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="source"/> is <see langword="null"/>.</exception>
    public static IEnumerable<T> WhereNotNull<T>(this IEnumerable<T?> source)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(source);
        return source.Where(static item => item is not null)!;
    }

    /// <summary>
    /// Returns the values of <paramref name="source"/> that are not <see langword="null"/>.
    /// </summary>
    /// <typeparam name="T">The underlying value type.</typeparam>
    /// <param name="source">The sequence to filter.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="source"/> is <see langword="null"/>.</exception>
    public static IEnumerable<T> WhereNotNull<T>(this IEnumerable<T?> source)
        where T : struct
    {
        ArgumentNullException.ThrowIfNull(source);
        return Iterate(source);

        static IEnumerable<T> Iterate(IEnumerable<T?> source)
        {
            foreach (var item in source)
            {
                if (item.HasValue)
                    yield return item.GetValueOrDefault();
            }
        }
    }
}

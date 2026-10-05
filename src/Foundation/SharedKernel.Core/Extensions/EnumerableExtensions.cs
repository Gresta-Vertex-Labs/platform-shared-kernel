using System.Diagnostics.CodeAnalysis;

namespace SharedKernel.Core.Extensions;

/// <summary>
/// Extension methods for <see cref="IEnumerable{T}"/>.
/// </summary>
/// <remarks>
/// The BCL already covers batching (<see cref="Enumerable.Chunk{TSource}(IEnumerable{TSource}, int)"/>), so it
/// is not duplicated here.
/// </remarks>
public static class EnumerableExtensions
{
    /// <summary>Determines whether a sequence is <see langword="null"/> or has no elements.</summary>
    /// <remarks>
    /// Uses the collection's count when it exposes one; otherwise reads at most one element. A lazy sequence that
    /// cannot be enumerated twice loses that element, so materialize such a sequence first.
    /// </remarks>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="source">The sequence to test.</param>
    /// <returns>
    /// <see langword="true"/> when <paramref name="source"/> is <see langword="null"/> or empty; otherwise
    /// <see langword="false"/>, in which case the compiler treats <paramref name="source"/> as non-null.
    /// </returns>
    /// <example>
    /// <code>
    /// if (!order.Lines.IsNullOrEmpty())
    ///     total = order.Lines.Sum(line =&gt; line.Amount);   // no nullable warning
    /// </code>
    /// </example>
    public static bool IsNullOrEmpty<T>([NotNullWhen(false)] this IEnumerable<T>? source)
    {
        if (source is null)
            return true;

        return source.TryGetNonEnumeratedCount(out var count) ? count == 0 : !source.Any();
    }

    /// <summary>Filters out <see langword="null"/> elements, narrowing the element type to non-nullable.</summary>
    /// <remarks>Execution is deferred: the sequence is read when the result is enumerated.</remarks>
    /// <typeparam name="T">The reference element type.</typeparam>
    /// <param name="source">The sequence to filter.</param>
    /// <returns>The non-null elements of <paramref name="source"/>, in order.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
    public static IEnumerable<T> WhereNotNull<T>(this IEnumerable<T?> source)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(source);
        return source.Where(static item => item is not null)!;
    }

    /// <summary>Filters out elements without a value, unwrapping the rest from <see cref="Nullable{T}"/>.</summary>
    /// <remarks>Execution is deferred: the sequence is read when the result is enumerated.</remarks>
    /// <typeparam name="T">The underlying value type.</typeparam>
    /// <param name="source">The sequence to filter.</param>
    /// <returns>The values of the elements of <paramref name="source"/> that have one, in order.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
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

using System.Linq.Expressions;

namespace SharedKernel.Domain.Specifications;

/// <summary>
/// Abstract base class for cursor/seek-pagination ("keyset pagination") query specifications —
/// the deep-pagination sibling of <see cref="PagedSpecification{T}"/>.
/// </summary>
/// <typeparam name="T">The type of domain entity this specification applies to.</typeparam>
/// <typeparam name="TKey">The comparable sort-key type used for cursor/seek pagination.</typeparam>
/// <remarks>
/// <para>
/// <typeparamref name="TKey"/> carries a <see langword="struct"/> constraint in addition to
/// <see cref="IComparable{T}"/>. This is required so that <see cref="AfterKey"/>'s <c>TKey?</c>
/// shape compiles to a genuine <see cref="Nullable{T}"/> — for a type parameter constrained only
/// by an interface (no <see langword="struct"/>/<see langword="class"/> split), C# erases <c>TKey?</c>
/// to plain <c>TKey</c> for value-type closures (confirmed empirically during implementation), which
/// would silently break the "null = first page" cursor contract for value-typed sort keys such as
/// <see cref="DateTimeOffset"/>, <see cref="int"/>, or <see cref="Guid"/> — by far the most common
/// keyset sort-key shapes. A reference-typed sort key (e.g. <see cref="string"/>) is not supported by
/// this base as a result; such a case should sort by a value-typed proxy column instead.
/// </para>
/// <para>
/// Offset pagination (<see cref="PagedSpecification{T}"/>) degrades on large, actively-written
/// result sets because <c>OFFSET</c> forces the database to walk and discard every skipped row.
/// Keyset pagination instead remembers the sort key (and identity) of the last row seen and asks
/// the database to seek directly past it — a <c>WHERE (SortKey, Id) &gt; (@cursor, @cursorId)</c>-shaped
/// predicate — which stays fast regardless of how deep the page is.
/// </para>
/// <para>
/// <strong>The Id tiebreaker is mandatory, never optional.</strong> The constructor always calls
/// <see cref="Specification{T}.ApplyThenBy"/> on the Id selector after the primary sort, in the same
/// direction as the primary sort. The seek predicate compares both key and Id in that one direction, so a
/// tiebreak sorted the other way would skip or repeat rows that share a sort key. A keyset page boundary is unsound without a unique,
/// deterministic sort order — if many rows share the same <typeparamref name="TKey"/> value, a
/// sort on <typeparamref name="TKey"/> alone cannot guarantee which of them comes "next".
/// </para>
/// <para>
/// <see cref="Specification{T}.Skip"/> is always fixed at <c>0</c> via
/// <see cref="Specification{T}.ApplyPaging"/> — the persistence-layer evaluator must ignore
/// <c>Skip</c> for a keyset specification and instead translate <see cref="AfterKey"/>/
/// <see cref="AfterId"/> into the seek predicate described above, composed with any expression
/// <see cref="ISpecification{T}.Criteria"/> via logical AND.
/// </para>
/// <para>
/// Concrete subclasses still call <see cref="Specification{T}.AddCriteria"/> in their own
/// constructor for filtering — this base governs ordering and paging shape only, composing
/// cleanly with existing <c>Criteria</c>/<c>Includes</c>.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public sealed class ActiveOrdersKeysetSpec : KeysetSpecification&lt;Order, DateTimeOffset&gt;
/// {
///     public ActiveOrdersKeysetSpec(DateTimeOffset? afterKey, object? afterId, int take)
///         : base(o =&gt; o.CreatedOn, o =&gt; o.Id, afterKey, afterId, descending: true, take)
///     {
///         AddCriteria(o =&gt; !o.IsDeleted);
///     }
/// }
/// </code>
/// </example>
public abstract class KeysetSpecification<T, TKey> : ReadOnlySpecification<T>
    where TKey : struct, IComparable<TKey>
{
    /// <summary>
    /// Initialises a new keyset/cursor-pagination specification.
    /// </summary>
    /// <param name="keySelector">The primary sort-key selector (e.g., a creation timestamp).</param>
    /// <param name="idSelector">
    /// The aggregate identity selector used as the mandatory deterministic tiebreaker.
    /// </param>
    /// <param name="afterKey">
    /// The sort-key value of the last row seen on the previous page, or <see langword="null"/> for
    /// the first page.
    /// </param>
    /// <param name="afterId">
    /// The identity value of the last row seen on the previous page, or <see langword="null"/> for
    /// the first page.
    /// </param>
    /// <param name="descending">
    /// <see langword="true"/> to sort by <paramref name="keySelector"/> descending;
    /// <see langword="false"/> for ascending.
    /// </param>
    /// <param name="take">The maximum number of entities to return. Must be at least 1.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="take"/> is less than 1.</exception>
    /// <exception cref="ArgumentException">
    /// Thrown when exactly one of <paramref name="afterKey"/>/<paramref name="afterId"/> is supplied —
    /// a cursor is atomic: both must be supplied together, or neither.
    /// </exception>
    protected KeysetSpecification(
        Expression<Func<T, TKey>> keySelector,
        Expression<Func<T, object>> idSelector,
        TKey? afterKey,
        object? afterId,
        bool descending,
        int take)
    {
        if (take < 1)
            throw new ArgumentOutOfRangeException(nameof(take), take,
                "Take must be greater than or equal to 1.");

        if (afterKey is null != afterId is null)
            throw new ArgumentException(
                "AfterKey and afterId must both be supplied or both be null — a cursor is atomic.",
                nameof(afterKey));

        AfterKey = afterKey;
        AfterId = afterId;
        Descending = descending;

        var objectKeySelector = ToObjectSelector(keySelector);
        if (descending)
            ApplyOrderByDescending(objectKeySelector);
        else
            ApplyOrderBy(objectKeySelector);

        // Mandatory tiebreaker, in the primary sort's direction so it matches the seek predicate.
        ApplyThenBy(idSelector, descending);

        ApplyPaging(0, take);
    }

    /// <summary>
    /// Gets the sort-key value of the last row seen on the previous page, or <see langword="null"/>
    /// for the first page (no seek predicate).
    /// </summary>
    public TKey? AfterKey { get; }

    /// <summary>
    /// Gets the identity value of the last row seen on the previous page, or <see langword="null"/>
    /// for the first page (no seek predicate).
    /// </summary>
    public object? AfterId { get; }

    /// <summary>
    /// Gets a value indicating whether this specification sorts by <see cref="AfterKey"/>'s selector
    /// in descending order.
    /// </summary>
    public bool Descending { get; }

    /// <summary>
    /// Converts a <typeparamref name="TKey"/>-typed key selector into an <see cref="object"/>-typed
    /// selector compatible with <see cref="Specification{T}.ApplyOrderBy"/>/
    /// <see cref="Specification{T}.ApplyOrderByDescending"/>, boxing value-typed keys as needed.
    /// </summary>
    private static Expression<Func<T, object>> ToObjectSelector(Expression<Func<T, TKey>> keySelector)
    {
        var convertedBody = Expression.Convert(keySelector.Body, typeof(object));
        return Expression.Lambda<Func<T, object>>(convertedBody, keySelector.Parameters);
    }
}

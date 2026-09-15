using System.Linq.Expressions;

namespace SharedKernel.Domain.Specifications;

/// <summary>
/// Base class for a read-only query that pages by seeking past a cursor (the sort key and identity key of
/// the last row already returned) instead of skipping an offset.
/// </summary>
/// <typeparam name="T">The entity type the query returns.</typeparam>
/// <typeparam name="TKey">
/// The primary sort key's type, such as <see cref="DateTimeOffset"/>. Must be a value type so that
/// <see cref="AfterKey"/> can be <see langword="null"/> on the first page; for a reference-typed column,
/// sort by a value-typed column instead.
/// </typeparam>
/// <remarks>
/// <para>
/// <b>Usage.</b> Use it for deep or live result sets, where offset paging with
/// <see cref="PagedSpecification{T}"/> gets slower with depth and shifts when rows change. Pass
/// <see langword="null"/> for both cursor values on the first page; for each later page pass the sort key
/// and identity key of the last row of the previous page.
/// </para>
/// <para>
/// <b>Ordering.</b> The constructor sets the primary sort on the key selector and then adds the identity
/// selector as the first <see cref="Specification{T}.ThenBys"/> key, in the <em>same</em> direction. The
/// identity tiebreak makes the order unique, so rows sharing a sort key are neither skipped nor repeated.
/// </para>
/// <para>
/// <b>Evaluation contract.</b> <see cref="Specification{T}.Skip"/> is always <c>0</c> and
/// <see cref="Specification{T}.Take"/> is the page size. An evaluator must ignore
/// <see cref="Specification{T}.Skip"/> and, when <see cref="AfterKey"/> is not <see langword="null"/>, AND
/// <see cref="Specification{T}.Criteria"/> with the seek predicate
/// <c>key &gt; AfterKey || (key == AfterKey &amp;&amp; id &gt; AfterId)</c>, using <c>&lt;</c> in both
/// comparisons when <see cref="Descending"/> is <see langword="true"/>. It reads the identity selector from
/// the first <see cref="Specification{T}.ThenBys"/> entry.
/// </para>
/// <para>
/// <b>Pitfall.</b> An evaluator that treats it as an ordinary specification ignores the cursor, applies
/// <c>Skip 0</c> and <see cref="Specification{T}.Take"/>, and returns the first page every time. Composing
/// with <see cref="SpecificationExtensions.And{T}"/> or its siblings is worse: the result has no cursor,
/// ordering or paging, so it returns every matching row. In a subclass, add filters with
/// <see cref="Specification{T}.AddCriteria"/>, but never call <see cref="Specification{T}.ApplyOrderBy"/>,
/// <see cref="Specification{T}.ApplyOrderByDescending"/> (both throw, the primary sort is already set),
/// <see cref="Specification{T}.ApplyThenBy"/> or <see cref="Specification{T}.ApplyPaging"/>.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public sealed class ActiveOrdersKeysetSpec : KeysetSpecification&lt;Order, DateTimeOffset&gt;
/// {
///     public ActiveOrdersKeysetSpec(DateTimeOffset? afterCreatedOn, OrderId? afterId, int take)
///         : base(o =&gt; o.CreatedOn, o =&gt; o.Id, afterCreatedOn, afterId, descending: true, take)
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
    /// Initializes a new keyset specification with its sort key, identity tiebreak, cursor, direction and page
    /// size.
    /// </summary>
    /// <param name="keySelector">
    /// The primary sort key selector, such as <c>o =&gt; o.CreatedOn</c>. Must not be <see langword="null"/>.
    /// </param>
    /// <param name="idSelector">
    /// The identity key selector, such as <c>o =&gt; o.Id</c>, used as the tiebreak. Must not be
    /// <see langword="null"/>, and must select a unique value.
    /// </param>
    /// <param name="afterKey">
    /// The sort key of the last row of the previous page, or <see langword="null"/> for the first page.
    /// </param>
    /// <param name="afterId">
    /// The identity key of the last row of the previous page, or <see langword="null"/> for the first page.
    /// Must have the same type <paramref name="idSelector"/> returns (for example <c>OrderId</c>, not its
    /// underlying <see cref="Guid"/>).
    /// </param>
    /// <param name="descending">
    /// <see langword="true"/> to sort both keys in descending order; <see langword="false"/> for ascending.
    /// </param>
    /// <param name="take">The maximum number of rows per page. Must be at least 1.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="take"/> is less than 1.</exception>
    /// <exception cref="ArgumentException">
    /// Exactly one of <paramref name="afterKey"/> and <paramref name="afterId"/> is <see langword="null"/>; a
    /// cursor supplies both values or neither.
    /// </exception>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="keySelector"/> or <paramref name="idSelector"/> is <see langword="null"/>.
    /// </exception>
    protected KeysetSpecification(
        Expression<Func<T, TKey>> keySelector,
        Expression<Func<T, object>> idSelector,
        TKey? afterKey,
        object? afterId,
        bool descending,
        int take)
    {
        ArgumentNullException.ThrowIfNull(keySelector);

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
    /// Gets the sort key of the last row of the previous page, or <see langword="null"/> on the first page,
    /// where no seek predicate applies.
    /// </summary>
    public TKey? AfterKey { get; }

    /// <summary>
    /// Gets the identity key of the last row of the previous page, or <see langword="null"/> on the first
    /// page; non-null exactly when <see cref="AfterKey"/> is non-null.
    /// </summary>
    public object? AfterId { get; }

    /// <summary>
    /// Gets a value indicating whether the sort key and the identity tiebreak are both sorted in descending
    /// order.
    /// </summary>
    public bool Descending { get; }

    /// <summary>
    /// Converts a <typeparamref name="TKey"/>-typed key selector into the <see cref="object"/>-typed selector
    /// the sort builders take, by wrapping its body in a conversion to <see cref="object"/>.
    /// </summary>
    private static Expression<Func<T, object>> ToObjectSelector(Expression<Func<T, TKey>> keySelector)
    {
        var convertedBody = Expression.Convert(keySelector.Body, typeof(object));
        return Expression.Lambda<Func<T, object>>(convertedBody, keySelector.Parameters);
    }
}

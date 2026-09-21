using System.Linq.Expressions;
using System.Reflection;
using SharedKernel.Contracts.Pagination;
using SharedKernel.Domain.StronglyTypedIds;

namespace SharedKernel.Persistence.EfCore.Specifications;

/// <summary>
/// Keyset (seek) paging over any EF Core query: orders by a sort key and an identity tiebreak, seeks past a
/// cursor position and reads one row beyond the page.
/// </summary>
/// <remarks>
/// <para>
/// <b>Usage.</b> Repositories use it for <c>ListKeysetAsync</c>; call it directly in a custom query. Pass the
/// source already filtered, without ordering or paging.
/// </para>
/// <para>
/// <b>SQL.</b> <c>WHERE key &gt; @key OR (key = @key AND id &gt; @id) ORDER BY key, id LIMIT @limit + 1</c>
/// (every comparison flipped when descending). The cursor values are SQL parameters. A strongly-typed identifier
/// is compared on its underlying value, so its value converter applies.
/// </para>
/// </remarks>
public static class KeysetQueryableExtensions
{
    /// <summary>
    /// Orders <paramref name="source"/> by <paramref name="keySelector"/> then <paramref name="idSelector"/>,
    /// seeks past <paramref name="after"/> and takes <paramref name="limit"/> + 1 rows.
    /// </summary>
    /// <typeparam name="T">The entity type.</typeparam>
    /// <typeparam name="TKey">The sort key's type; must be comparable and not nullable.</typeparam>
    /// <typeparam name="TId">The identity's type: comparable, or a strongly-typed identifier over a comparable value.</typeparam>
    /// <param name="source">The filtered query, without ordering or paging.</param>
    /// <param name="keySelector">The sort key.</param>
    /// <param name="idSelector">The unique identity, used as the tiebreak.</param>
    /// <param name="after">The key and identity of the last row of the previous page, or <see langword="null"/> for the first page.</param>
    /// <param name="descending"><see langword="true"/> to sort both keys descending.</param>
    /// <param name="limit">The page size. Must be at least 1.</param>
    /// <returns>
    /// The query for up to <paramref name="limit"/> + 1 rows; pass the result to
    /// <see cref="CursorPagedList{T}.FromLookahead"/> to trim the look-ahead row and build the next cursor.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="limit"/> is less than 1.</exception>
    /// <exception cref="InvalidOperationException">A key type is neither comparable nor a strongly-typed identifier.</exception>
    public static IQueryable<T> ToKeysetPage<T, TKey, TId>(
        this IQueryable<T> source,
        Expression<Func<T, TKey>> keySelector,
        Expression<Func<T, TId>> idSelector,
        CursorPosition<TKey, TId>? after,
        bool descending,
        int limit)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(keySelector);
        ArgumentNullException.ThrowIfNull(idSelector);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);

        if (after is { } position)
            source = source.Where(BuildSeekPredicate(keySelector, idSelector, position, descending));

        var ordered = descending
            ? source.OrderByDescending(keySelector).ThenByDescending(idSelector)
            : source.OrderBy(keySelector).ThenBy(idSelector);

        return ordered.Take(limit + 1);
    }

    // key > @key OR (key == @key AND id > @id), "<" when descending, over one shared parameter. The cursor
    // values are read through closure-shaped holder fields so EF Core turns them into SQL parameters.
    private static Expression<Func<T, bool>> BuildSeekPredicate<T, TKey, TId>(
        Expression<Func<T, TKey>> keySelector,
        Expression<Func<T, TId>> idSelector,
        CursorPosition<TKey, TId> after,
        bool descending)
    {
        var parameter = keySelector.Parameters[0];
        var key = keySelector.Body;
        var id = new ParameterReplacer(idSelector.Parameters[0], parameter).Visit(idSelector.Body)!;

        var afterKey = Expression.Field(Expression.Constant(new Holder<TKey>(after.Key)), nameof(Holder<TKey>.Value));
        var afterId = Expression.Field(Expression.Constant(new Holder<TId>(after.Id)), nameof(Holder<TId>.Value));

        var keyBeyond = Compare(key, afterKey, descending);
        var keyEqual = Expression.Equal(Unwrap(key), Unwrap(afterKey));
        var idBeyond = Compare(id, afterId, descending);

        return Expression.Lambda<Func<T, bool>>(
            Expression.OrElse(keyBeyond, Expression.AndAlso(keyEqual, idBeyond)),
            parameter);
    }

    // left.CompareTo(right) > 0 (or < 0), which EF Core translates to a plain SQL comparison for every
    // comparable type, including those without > / < operators such as Guid and string. A strongly-typed
    // identifier is unwrapped to its underlying value first.
    private static BinaryExpression Compare(Expression left, Expression right, bool descending)
    {
        left = Unwrap(left);
        right = Unwrap(right);

        var compareTo = left.Type.GetMethod(nameof(IComparable<int>.CompareTo), [left.Type])
            ?? throw new InvalidOperationException(
                $"Type '{left.Type.Name}' is not comparable and is not a StronglyTypedId<TValue>; it cannot be "
                + "a keyset sort key or identity tiebreak. Nullable keys are not supported.");

        var comparison = Expression.Call(left, compareTo, right);
        var zero = Expression.Constant(0);
        return descending ? Expression.LessThan(comparison, zero) : Expression.GreaterThan(comparison, zero);
    }

    // Converts a StronglyTypedId<TValue> operand to TValue through its explicit operator (declared on the
    // closed generic base, so it is looked up there); any other operand is returned unchanged.
    private static Expression Unwrap(Expression operand)
    {
        for (var type = operand.Type; type is not null; type = type.BaseType)
        {
            if (!type.IsGenericType || type.GetGenericTypeDefinition() != typeof(StronglyTypedId<>))
                continue;

            var conversion = type.GetMethod("op_Explicit", BindingFlags.Public | BindingFlags.Static, [type]);
            if (conversion is not null)
                return Expression.Convert(operand, type.GetGenericArguments()[0], conversion);
        }

        return operand;
    }

    private sealed class Holder<TValue>(TValue value)
    {
        public readonly TValue Value = value;
    }

    private sealed class ParameterReplacer(ParameterExpression source, ParameterExpression target) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) =>
            node == source ? target : base.VisitParameter(node);
    }
}

using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Text.Json;
using SharedKernel.Contracts.Pagination;
using SharedKernel.Core.Exceptions;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Domain.StronglyTypedIds.Serialization;

namespace SharedKernel.Persistence.EfCore.Repositories;

/// <summary>
/// Expressions and cached accessors the repositories build once per aggregate type rather than per call.
/// </summary>
/// <typeparam name="TAggregate">The aggregate root type.</typeparam>
/// <typeparam name="TId">The aggregate's identity type.</typeparam>
internal static class RepositoryExpressions<TAggregate, TId>
    where TAggregate : class, IAggregateRoot<TId>
    where TId : notnull
{
    // Compiled sort-key accessors, keyed by member path ("CreatedOn", "Total.Amount"): a keyset page reads
    // the last row's key in memory to build the next cursor, and compiling per call was A27.
    private static readonly ConcurrentDictionary<string, Delegate> KeyAccessors = new(StringComparer.Ordinal);

    /// <summary>Gets <c>e =&gt; e.Id</c>, built once.</summary>
    internal static Expression<Func<TAggregate, TId>> IdSelector { get; } = BuildIdSelector();

    /// <summary>Builds <c>e =&gt; e.Id == @id</c> with the identity as a SQL parameter.</summary>
    internal static Expression<Func<TAggregate, bool>> ById(TId id)
    {
        var holder = new ValueHolder<TId>(id);
        var parameter = IdSelector.Parameters[0];
        var value = Expression.Field(Expression.Constant(holder), nameof(ValueHolder<TId>.Value));
        return Expression.Lambda<Func<TAggregate, bool>>(Expression.Equal(IdSelector.Body, value), parameter);
    }

    /// <summary>Builds <c>e =&gt; e.{name}</c> for a property the aggregate type declares or inherits.</summary>
    internal static Expression<Func<TAggregate, TProperty>> Property<TProperty>(string name)
    {
        var parameter = Expression.Parameter(typeof(TAggregate), "e");
        return Expression.Lambda<Func<TAggregate, TProperty>>(Expression.Property(parameter, name), parameter);
    }

    /// <summary>Builds <c>e =&gt; !e.IsDeleted</c> for a soft-deletable aggregate.</summary>
    internal static Expression<Func<TAggregate, bool>> NotDeleted()
    {
        var isDeleted = Property<bool>(nameof(ISoftDeletable.IsDeleted));
        return Expression.Lambda<Func<TAggregate, bool>>(Expression.Not(isDeleted.Body), isDeleted.Parameters);
    }

    /// <summary>
    /// Returns the cached compiled accessor for <paramref name="keySelector"/>, which must be a plain member path.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="keySelector"/> is not a plain member path.</exception>
    internal static Func<TAggregate, TKey> KeyAccessor<TKey>(Expression<Func<TAggregate, TKey>> keySelector)
    {
        var path = MemberPath.TryGet(keySelector)
            ?? throw new ArgumentException(
                $"A keyset sort key must be a plain member path such as o => o.CreatedOn, but got '{keySelector}'.",
                nameof(keySelector));

        return (Func<TAggregate, TKey>)KeyAccessors.GetOrAdd(
            path + "|" + typeof(TKey).FullName,
            static (_, selector) => selector.Compile(),
            keySelector);
    }

    private static Expression<Func<TAggregate, TId>> BuildIdSelector()
    {
        var parameter = Expression.Parameter(typeof(TAggregate), "e");
        return Expression.Lambda<Func<TAggregate, TId>>(
            Expression.Property(parameter, nameof(IAggregateRoot<TId>.Id)), parameter);
    }
}

/// <summary>Cursor encoding shared by every repository.</summary>
internal static class KeysetCursor
{
    /// <summary>
    /// Serializer options for cursor values: strongly-typed identifiers are written as their bare value.
    /// </summary>
    internal static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new StronglyTypedIdJsonConverterFactory() },
    };

    /// <summary>Decodes the request's cursor, or returns <see langword="null"/> for the first page.</summary>
    /// <exception cref="ValidationException">The cursor is malformed or holds values of other types.</exception>
    internal static CursorPosition<TKey, TId>? Decode<TKey, TId>(CursorPageRequest page)
    {
        if (page.Cursor is null)
            return null;

        var decoded = PageCursor.Decode<TKey, TId>(page.Cursor, Options);
        if (decoded.IsFailure)
            throw new ValidationException(decoded.Error);

        return decoded.Value;
    }

    /// <summary>Encodes a key and identity as an opaque cursor.</summary>
    internal static string Encode<TKey, TId>(TKey key, TId id) => PageCursor.Encode(key, id, Options);
}

/// <summary>Reads a plain member path (<c>o =&gt; o.Total.Amount</c> → <c>"Total.Amount"</c>).</summary>
internal static class MemberPath
{
    /// <summary>
    /// Returns the member names from the parameter outwards, or <see langword="null"/> when the body is not a
    /// plain member chain (conversions aside).
    /// </summary>
    internal static IReadOnlyList<string>? TryGetSegments(LambdaExpression selector)
    {
        var names = new List<string>();
        var current = StripConvert(selector.Body);

        while (current is MemberExpression member)
        {
            names.Add(member.Member.Name);
            if (member.Expression is null)
                return null;

            current = StripConvert(member.Expression);
        }

        if (current is not ParameterExpression parameter || parameter != selector.Parameters[0] || names.Count == 0)
            return null;

        names.Reverse();
        return names;
    }

    /// <summary>Returns the dot-separated member path, or <see langword="null"/>.</summary>
    internal static string? TryGet(LambdaExpression selector) =>
        TryGetSegments(selector) is { } segments ? string.Join('.', segments) : null;

    private static Expression StripConvert(Expression expression)
    {
        while (expression is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked or ExpressionType.TypeAs } unary)
            expression = unary.Operand;

        return expression;
    }
}

/// <summary>
/// A closure-shaped holder: EF Core turns a field read on a constant holder into a SQL parameter, where a
/// bare constant would be inlined as a literal and defeat query-plan caching.
/// </summary>
internal sealed class ValueHolder<TValue>(TValue value)
{
    public readonly TValue Value = value;
}

/// <summary>A keyset row read in one SQL <c>SELECT</c>: the projected item plus the cursor columns.</summary>
internal sealed class KeysetRow<TItem, TKey, TId>
{
    public TItem Item { get; init; } = default!;

    public TKey Key { get; init; } = default!;

    public TId Id { get; init; } = default!;
}

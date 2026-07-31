namespace SharedKernel.Persistence.Abstractions.Specifications;

/// <summary>
/// A single page of results returned by <c>IReadRepository&lt;TAggregate,TId&gt;.ListKeysetAsync&lt;TKey&gt;</c>,
/// carrying the cursor values needed to fetch the next page.
/// </summary>
/// <typeparam name="TAggregate">The aggregate root type this page contains.</typeparam>
/// <typeparam name="TKey">The comparable sort-key type used for cursor/seek pagination.</typeparam>
/// <param name="Items">The current page's items, in the specification's declared sort order.</param>
/// <param name="NextAfterKey">
/// The sort-key value to pass as the next page's <c>afterKey</c> cursor argument, or
/// <see langword="null"/> when <paramref name="HasMore"/> is <see langword="false"/>.
/// </param>
/// <param name="NextAfterId">
/// The identity value to pass as the next page's <c>afterId</c> cursor argument, or
/// <see langword="null"/> when <paramref name="HasMore"/> is <see langword="false"/>.
/// </param>
/// <param name="HasMore">
/// <see langword="true"/> when at least one further page exists beyond this one.
/// </param>
/// <remarks>
/// <para>
/// <c>NextAfterKey</c>/<c>NextAfterId</c> are <see langword="null"/>/<c>default</c> exactly when
/// <c>HasMore == false</c> (no further page) — callers pass them straight back as the next
/// <c>KeysetSpecification&lt;T,TKey&gt;</c>'s <c>afterKey</c>/<c>afterId</c> constructor arguments to
/// fetch the following page.
/// </para>
/// <para>
/// Zero ORM dependency — BCL types plus the already-referenced
/// <c>SharedKernel.Domain</c>/<c>SharedKernel.Domain.Specifications.KeysetSpecification&lt;T,TKey&gt;</c> type.
/// </para>
/// </remarks>
public sealed record KeysetPage<TAggregate, TKey>(
    IReadOnlyList<TAggregate> Items,
    TKey? NextAfterKey,
    object? NextAfterId,
    bool HasMore)
    where TKey : struct, IComparable<TKey>;

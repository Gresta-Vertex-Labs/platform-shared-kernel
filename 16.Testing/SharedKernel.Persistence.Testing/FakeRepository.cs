using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Text.Json;
using SharedKernel.Contracts.Pagination;
using SharedKernel.Core.Exceptions;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Domain.Specifications;
using SharedKernel.Domain.StronglyTypedIds.Serialization;
using SharedKernel.Persistence.Abstractions.Repositories;

namespace SharedKernel.Persistence.Testing;

/// <summary>
/// In-memory fake of <see cref="IRepository{TAggregate, TId}"/> (and therefore
/// <see cref="IReadRepository{TAggregate, TId}"/>) for unit tests.
/// </summary>
/// <typeparam name="TAggregate">The aggregate root type.</typeparam>
/// <typeparam name="TId">The aggregate's identity type.</typeparam>
/// <remarks>
/// <para>
/// <b>Fidelity.</b> Specifications are evaluated in memory with the production rules: soft-deleted aggregates are
/// hidden unless the specification includes them, Distinct before ordering, Skip/Take last and only with a primary
/// sort, call-site paging (<see cref="ListPagedAsync"/>/<see cref="ListKeysetAsync{TKey}"/>) rejects a
/// specification that pages or (keyset) orders itself, and keyset cursors are encoded exactly as production encodes
/// them, so a cursor from this fake decodes against the real repository.
/// </para>
/// <para>
/// <b>Differences, by design.</b> Writes apply immediately (there is no change tracker to stage them), so tracked
/// and untracked reads return the same instances. <see cref="AddAsync"/> fails fast on a duplicate key and
/// <see cref="UpdateAsync(TAggregate, CancellationToken)"/> on a missing one, to surface test-authoring bugs.
/// <see cref="DeleteAsync(TAggregate, CancellationToken)"/> always removes. The expected-version overload of <c>UpdateAsync</c> cannot check a
/// row version and behaves like the plain overload. Includes and split queries are no-ops.
/// </para>
/// <para>
/// <b>Transactions.</b> Registered with <c>AddFakeRepository</c> next to <c>AddFakeUnitOfWork</c>, the repository
/// takes part in the fake transaction: a rollback — a failed <c>Result</c>, an exception, or a replay forced by
/// <see cref="FakeUnitOfWork.TransientFailures"/> — puts back the aggregates the repository held when the transaction
/// started. It restores which aggregates are stored, not changes made to an aggregate object in place.
/// </para>
/// </remarks>
public sealed class FakeRepository<TAggregate, TId> : IRepository<TAggregate, TId>, IFakeTransactionParticipant
    where TAggregate : IAggregateRoot<TId>
    where TId : notnull
{
    private static readonly JsonSerializerOptions CursorOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new StronglyTypedIdJsonConverterFactory() },
    };

    private readonly ConcurrentDictionary<TId, TAggregate> _items = new();
    private readonly Func<TAggregate, TId> _idSelector;

    /// <summary>Initializes a new fake repository.</summary>
    /// <param name="idSelector">Derives an aggregate's identity, typically <c>a =&gt; a.Id</c>.</param>
    /// <param name="seed">Optional initial aggregates, applied via <see cref="Seed"/>.</param>
    public FakeRepository(Func<TAggregate, TId> idSelector, IEnumerable<TAggregate>? seed = null)
    {
        ArgumentNullException.ThrowIfNull(idSelector);
        _idSelector = idSelector;

        if (seed is not null)
            Seed(seed);
    }

    /// <summary>Gets a live view of every stored aggregate, soft-deleted ones included, keyed by identity.</summary>
    public IReadOnlyDictionary<TId, TAggregate> Items => _items;

    /// <summary>
    /// Gets or sets whether the write members throw <see cref="InvalidOperationException"/> instead of acting.
    /// Reads, <see cref="Seed"/> and <see cref="Reset"/> are never affected.
    /// </summary>
    public bool SimulateFailure { get; set; }

    // ----- reads -----

    /// <inheritdoc />
    public Task<TAggregate?> GetByIdAsync(TId id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_items.TryGetValue(id, out var value) && !IsSoftDeleted(value) ? value : default);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<TAggregate>> GetByIdsAsync(IEnumerable<TId> ids, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ids);
        cancellationToken.ThrowIfCancellationRequested();

        var result = new List<TAggregate>();
        foreach (var id in ids)
        {
            if (_items.TryGetValue(id, out var value) && !IsSoftDeleted(value))
                result.Add(value);
        }

        return Task.FromResult<IReadOnlyList<TAggregate>>(result);
    }

    /// <inheritdoc />
    public Task<bool> ExistsAsync(TId id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_items.TryGetValue(id, out var value) && !IsSoftDeleted(value));
    }

    /// <inheritdoc />
    public Task<TAggregate?> FirstOrDefaultAsync(ISpecification<TAggregate> spec, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Apply(spec).FirstOrDefault());
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<TAggregate>> ListAsync(ISpecification<TAggregate> spec, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<TAggregate>>(Apply(spec).ToList());
    }

    /// <inheritdoc />
    public Task<long> CountAsync(ISpecification<TAggregate> spec, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(FilterAndOrder(spec).LongCount());
    }

    /// <inheritdoc />
    public Task<bool> AnyAsync(ISpecification<TAggregate> spec, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Apply(spec).Any());
    }

    /// <inheritdoc />
    public Task<PagedList<TAggregate>> ListPagedAsync(
        ISpecification<TAggregate> spec,
        PageRequest page,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Page(spec, page, item => item));
    }

    /// <inheritdoc />
    public Task<CursorPagedList<TAggregate>> ListKeysetAsync<TKey>(
        ISpecification<TAggregate> spec,
        CursorPageRequest page,
        Expression<Func<TAggregate, TKey>> keySelector,
        bool descending = false,
        CancellationToken cancellationToken = default)
        where TKey : notnull
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Keyset(spec, page, keySelector, descending, item => item));
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<TAggregate> StreamAsync(
        ISpecification<TAggregate> spec,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var item in Apply(spec).ToList())
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return item;
            await Task.Yield();
        }
    }

    /// <inheritdoc />
    public Task<TResult?> FirstOrDefaultProjectedAsync<TResult>(
        IProjectionSpecification<TAggregate, TResult> spec,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Apply(spec).Select(spec.Selector.Compile()).FirstOrDefault());
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<TResult>> ListProjectedAsync<TResult>(
        IProjectionSpecification<TAggregate, TResult> spec,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<TResult>>(Apply(spec).Select(spec.Selector.Compile()).ToList());
    }

    /// <inheritdoc />
    public Task<PagedList<TResult>> ListPagedProjectedAsync<TResult>(
        IProjectionSpecification<TAggregate, TResult> spec,
        PageRequest page,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Page(spec, page, spec.Selector.Compile()));
    }

    /// <inheritdoc />
    public Task<CursorPagedList<TResult>> ListKeysetProjectedAsync<TKey, TResult>(
        IProjectionSpecification<TAggregate, TResult> spec,
        CursorPageRequest page,
        Expression<Func<TAggregate, TKey>> keySelector,
        bool descending = false,
        CancellationToken cancellationToken = default)
        where TKey : notnull
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Keyset(spec, page, keySelector, descending, spec.Selector.Compile()));
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<TResult> StreamProjectedAsync<TResult>(
        IProjectionSpecification<TAggregate, TResult> spec,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var selector = spec.Selector.Compile();
        foreach (var item in Apply(spec).Select(selector).ToList())
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return item;
            await Task.Yield();
        }
    }

    // ----- writes -----

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">Simulating failure, or the key already exists.</exception>
    public Task AddAsync(TAggregate aggregate, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfSimulatingFailure();

        var key = _idSelector(aggregate);
        if (!_items.TryAdd(key, aggregate))
            throw new InvalidOperationException($"An aggregate with key '{key}' already exists.");

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task AddRangeAsync(IEnumerable<TAggregate> aggregates, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(aggregates);
        foreach (var aggregate in aggregates)
            await AddAsync(aggregate, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">Simulating failure.</exception>
    /// <exception cref="System.Collections.Generic.KeyNotFoundException">No aggregate with the key exists.</exception>
    public Task UpdateAsync(TAggregate aggregate, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfSimulatingFailure();

        var key = _idSelector(aggregate);
        if (!_items.ContainsKey(key))
            throw new System.Collections.Generic.KeyNotFoundException($"No aggregate with key '{key}' exists to update.");

        _items[key] = aggregate;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    /// <remarks>The fake has no row versions; this behaves like the plain overload.</remarks>
    public Task UpdateAsync(TAggregate aggregate, EntityVersion expectedVersion, CancellationToken cancellationToken = default) =>
        UpdateAsync(aggregate, cancellationToken);

    /// <inheritdoc />
    public async Task UpdateRangeAsync(IEnumerable<TAggregate> aggregates, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(aggregates);
        foreach (var aggregate in aggregates)
            await UpdateAsync(aggregate, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>Always removes the aggregate, soft-deletable or not; a missing key is a no-op.</remarks>
    public Task DeleteAsync(TAggregate aggregate, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfSimulatingFailure();

        _items.TryRemove(_idSelector(aggregate), out _);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    /// <remarks>The fake has no row versions; this behaves like the plain overload.</remarks>
    public Task DeleteAsync(TAggregate aggregate, EntityVersion expectedVersion, CancellationToken cancellationToken = default) =>
        DeleteAsync(aggregate, cancellationToken);

    /// <inheritdoc />
    public async Task DeleteRangeAsync(IEnumerable<TAggregate> aggregates, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(aggregates);
        foreach (var aggregate in aggregates)
            await DeleteAsync(aggregate, cancellationToken).ConfigureAwait(false);
    }

    // ----- test setup -----

    /// <summary>Stores <paramref name="aggregates"/> directly, overwriting equal keys; ignores <see cref="SimulateFailure"/>.</summary>
    /// <param name="aggregates">The aggregates to store.</param>
    public void Seed(IEnumerable<TAggregate> aggregates)
    {
        ArgumentNullException.ThrowIfNull(aggregates);
        foreach (var aggregate in aggregates)
            _items[_idSelector(aggregate)] = aggregate;
    }

    /// <summary>Clears the store; <see cref="SimulateFailure"/> is left unchanged.</summary>
    public void Reset() => _items.Clear();

    object IFakeTransactionParticipant.Capture() => new Dictionary<TId, TAggregate>(_items);

    void IFakeTransactionParticipant.Restore(object snapshot)
    {
        _items.Clear();
        foreach (var (key, aggregate) in (Dictionary<TId, TAggregate>)snapshot)
            _items[key] = aggregate;
    }

    // ----- in-memory evaluation -----

    private void ThrowIfSimulatingFailure()
    {
        if (SimulateFailure)
            throw new InvalidOperationException("FakeRepository was configured to simulate failure.");
    }

    private static bool IsSoftDeleted(TAggregate item) => item is ISoftDeletable { IsDeleted: true };

    // Soft-delete filter, criteria, Distinct, ordering: everything but Skip/Take.
    private IEnumerable<TAggregate> FilterAndOrder(ISpecification<TAggregate> spec)
    {
        ArgumentNullException.ThrowIfNull(spec);

        IEnumerable<TAggregate> query = _items.Values;

        if (!spec.IncludeDeleted)
            query = query.Where(item => !IsSoftDeleted(item));

        if (spec.Criteria is not null)
            query = query.Where(spec.Criteria.Compile());

        if (spec.IsDistinct)
            query = query.Distinct();

        IOrderedEnumerable<TAggregate>? ordered = null;
        if (spec.OrderBy is not null)
            ordered = query.OrderBy(spec.OrderBy.Compile());
        else if (spec.OrderByDescending is not null)
            ordered = query.OrderByDescending(spec.OrderByDescending.Compile());

        if (ordered is null)
            return query;

        foreach (var (keySelector, descending) in spec.ThenBys)
        {
            var compiled = keySelector.Compile();
            ordered = descending ? ordered.ThenByDescending(compiled) : ordered.ThenBy(compiled);
        }

        return ordered;
    }

    private IEnumerable<TAggregate> Apply(ISpecification<TAggregate> spec)
    {
        var query = FilterAndOrder(spec);

        if ((spec.Skip.HasValue || spec.Take.HasValue) && spec.OrderBy is null && spec.OrderByDescending is null)
            throw new InvalidOperationException($"'{spec.GetType().Name}' declares Skip/Take but no primary sort.");

        if (spec.Skip is { } skip)
            query = query.Skip(skip);

        if (spec.Take is { } take)
            query = query.Take(take);

        return query;
    }

    private PagedList<TItem> Page<TItem>(ISpecification<TAggregate> spec, PageRequest page, Func<TAggregate, TItem> map)
    {
        ArgumentNullException.ThrowIfNull(page);
        EnsureNotSelfPaged(spec);

        if (spec.OrderBy is null && spec.OrderByDescending is null)
            throw new InvalidOperationException($"'{spec.GetType().Name}' has no primary sort; offset pages would be unstable.");

        var all = FilterAndOrder(spec).ToList();
        var items = all.Skip(page.Offset).Take(page.PageSize).Select(map).ToList();
        return PagedList<TItem>.Create(items, page, all.Count);
    }

    private CursorPagedList<TItem> Keyset<TKey, TItem>(
        ISpecification<TAggregate> spec,
        CursorPageRequest page,
        Expression<Func<TAggregate, TKey>> keySelector,
        bool descending,
        Func<TAggregate, TItem> map)
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(keySelector);
        EnsureNotSelfPaged(spec);

        if (spec.OrderBy is not null || spec.OrderByDescending is not null)
            throw new InvalidOperationException($"'{spec.GetType().Name}' declares an ordering; keyset pages order by the key and identity.");

        var key = keySelector.Compile();
        var direction = descending ? -1 : 1;

        int Compare(TKey leftKey, TId leftId, TKey rightKey, TId rightId)
        {
            var byKey = Comparer<object>.Default.Compare(Unwrap(leftKey), Unwrap(rightKey));
            return direction * (byKey != 0 ? byKey : Comparer<object>.Default.Compare(Unwrap(leftId), Unwrap(rightId)));
        }

        var rows = FilterAndOrder(spec).Select(item => (Item: item, Key: key(item), Id: _idSelector(item))).ToList();
        rows.Sort((a, b) => Compare(a.Key, a.Id, b.Key, b.Id));

        if (page.Cursor is not null)
        {
            var decoded = PageCursor.Decode<TKey, TId>(page.Cursor, CursorOptions);
            if (decoded.IsFailure)
                throw new ValidationException(decoded.Error);

            var after = decoded.Value;
            rows = rows.Where(r => Compare(r.Key, r.Id, after.Key, after.Id) > 0).ToList();
        }

        var lookahead = rows.Take(page.Limit + 1).ToList();
        var cursorPage = CursorPagedList<(TAggregate Item, TKey Key, TId Id)>.FromLookahead(
            lookahead, page.Limit, last => PageCursor.Encode(last.Key, last.Id, CursorOptions));

        return cursorPage.Map(row => map(row.Item));
    }

    private static void EnsureNotSelfPaged(ISpecification<TAggregate> spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        if (spec.Skip.HasValue || spec.Take.HasValue)
            throw new InvalidOperationException($"'{spec.GetType().Name}' declares Skip/Take; page with the page request instead.");
    }

    // A strongly-typed identifier compares by its underlying value.
    private static object? Unwrap(object? value)
    {
        if (value is null)
            return null;

        foreach (var contract in value.GetType().GetInterfaces())
        {
            if (contract.IsGenericType && contract.GetGenericTypeDefinition() == typeof(IStronglyTypedId<>))
                return contract.GetProperty(nameof(IStronglyTypedId<int>.Value))!.GetValue(value);
        }

        return value;
    }
}

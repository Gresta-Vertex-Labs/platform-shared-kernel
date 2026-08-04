using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using SharedKernel.Contracts.Pagination;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Domain.Specifications;
using SharedKernel.Persistence.Abstractions.Repositories;
using SharedKernel.Persistence.Abstractions.Specifications;

namespace SharedKernel.Testing.Persistence;

/// <summary>
/// In-memory fake implementation of both <see cref="IRepository{TAggregate, TId}"/> and
/// <see cref="IReadRepository{TAggregate, TId}"/> (<c>06.Persistence</c>) for use in unit tests.
/// </summary>
/// <typeparam name="TAggregate">The aggregate root type. Must implement <see cref="IAggregateRoot{TId}"/>.</typeparam>
/// <typeparam name="TId">The aggregate's identity type. Must be non-null.</typeparam>
/// <remarks>
/// <para>
/// A SINGLE type implements BOTH <see cref="IRepository{TAggregate, TId}"/> and
/// <see cref="IReadRepository{TAggregate, TId}"/> — a deliberate divergence from production's
/// <c>EfRepository</c>/<c>EfReadRepository</c> two-class split. That split exists to support
/// read-replica routing (WO-053/P-338), a distinction meaningless for a single in-memory collection.
/// Collapsing both interfaces into one type guarantees trivial, correct read-after-write consistency
/// with no shared-store wiring needed.
/// </para>
/// <para>
/// The constructor requires a caller-supplied <paramref name="idSelector"/> because
/// <see cref="IAggregateRoot{TId}"/> exposes no <c>.Id</c> member at all — <see cref="IEntity{TId}"/>
/// is a zero-member marker interface. Production solves this identical problem via a compiled
/// <c>Expression.Property("Id")</c> tree per closed generic type; this fake takes the simpler
/// caller-supplied-delegate route instead, mirroring <see cref="FakeDbConnectionFactory"/>'s own
/// "caller-supplied delegate over hand-rolled substitute" precedent.
/// </para>
/// <para>
/// Every write is applied IMMEDIATELY — there is no <c>ChangeTracker</c>-style staging the way real
/// EF Core stages mutations until <c>SaveChangesAsync</c> is called. <see cref="FakeUnitOfWork"/> (the
/// <c>06.Persistence</c>-shaped one, in this same namespace) is fully INDEPENDENT of this type, with
/// no constructor coupling — a test asserting "the handler called <c>SaveChangesAsync</c> exactly
/// once" asserts on <c>FakeUnitOfWork.SaveChangesCallCount</c> directly.
/// </para>
/// <para>
/// <b>Scope lock:</b> <c>IRestorableRepository&lt;TAggregate, TId&gt;</c> — a sibling interface
/// dispatched to <c>06.Persistence</c> in the SAME work order (WO-053/P-337) — is deliberately NOT
/// implemented by this type. Extending this fake to additionally implement
/// <c>IRestorableRepository&lt;TAggregate, TId&gt;</c> is a natural, additive future follow-up once a
/// concrete consumer needs it — not undertaken here, since P-335's own acceptance criteria name only
/// <see cref="IRepository{TAggregate, TId}"/>/<see cref="IReadRepository{TAggregate, TId}"/>.
/// </para>
/// </remarks>
public sealed class FakeRepository<TAggregate, TId> : IRepository<TAggregate, TId>, IReadRepository<TAggregate, TId>
    where TAggregate : IAggregateRoot<TId>
    where TId : notnull
{
    private readonly ConcurrentDictionary<TId, TAggregate> _items = new();
    private readonly Func<TAggregate, TId> _idSelector;

    /// <summary>
    /// Initialises a new <see cref="FakeRepository{TAggregate, TId}"/>.
    /// </summary>
    /// <param name="idSelector">
    /// Derives an aggregate's identity value. Mandatory — <see cref="IAggregateRoot{TId}"/> exposes no
    /// <c>.Id</c> member, so this fake cannot derive a key from a bare <typeparamref name="TAggregate"/>
    /// any other way. Used only by the write-side members that receive a bare aggregate; every
    /// read-side member either receives <typeparamref name="TId"/> directly or drives ordering/paging
    /// entirely from the specification's own already-compiled expressions.
    /// </param>
    /// <param name="seed">Optional initial aggregates, applied via <see cref="Seed"/>.</param>
    public FakeRepository(Func<TAggregate, TId> idSelector, IEnumerable<TAggregate>? seed = null)
    {
        ArgumentNullException.ThrowIfNull(idSelector);
        _idSelector = idSelector;

        if (seed is not null)
            Seed(seed);
    }

    /// <summary>Gets a live, read-through snapshot of every aggregate currently stored, keyed by identity.</summary>
    public IReadOnlyDictionary<TId, TAggregate> Items => _items;

    /// <summary>
    /// Gets or sets whether the six write-side mutating members (<see cref="AddAsync"/>,
    /// <see cref="UpdateAsync"/>, <see cref="DeleteAsync"/>, <see cref="AddRangeAsync"/>,
    /// <see cref="UpdateRangeAsync"/>, <see cref="DeleteRangeAsync"/>) should throw
    /// <see cref="InvalidOperationException"/> instead of performing the operation. The three
    /// pure-lookup members (<see cref="GetByIdAsync"/>, <see cref="ExistsAsync"/>,
    /// <see cref="GetBySpecAsync"/>) and every read-side member are never gated by this flag.
    /// <see cref="Seed"/> and <see cref="Reset"/> bypass it entirely.
    /// </summary>
    public bool SimulateFailure { get; set; }

    // ----- IRepository<TAggregate, TId> (write side) -----

    /// <inheritdoc />
    /// <remarks>Never soft-delete-filtered — a direct dictionary lookup by key.</remarks>
    public Task<TAggregate?> GetByIdAsync(TId id, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(_items.TryGetValue(id, out var value) ? value : default);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Shared with <see cref="IReadRepository{TAggregate, TId}.GetBySpecAsync"/> — a single
    /// implementation satisfies both interfaces since their signatures are identical.
    /// </remarks>
    public Task<TAggregate?> GetBySpecAsync(ISpecification<TAggregate> spec, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(ApplySpecification(spec).FirstOrDefault());
    }

    /// <inheritdoc />
    /// <remarks>Never soft-delete-filtered — a direct dictionary lookup by key.</remarks>
    public Task<bool> ExistsAsync(TId id, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(_items.ContainsKey(id));
    }

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">
    /// Thrown when <see cref="SimulateFailure"/> is <see langword="true"/>, or when an aggregate with
    /// the same derived key already exists.
    /// </exception>
    public Task AddAsync(TAggregate aggregate, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        ct.ThrowIfCancellationRequested();
        ThrowIfSimulatingFailure();

        var key = _idSelector(aggregate);
        if (!_items.TryAdd(key, aggregate))
            throw new InvalidOperationException($"An aggregate with key '{key}' already exists.");

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    /// <remarks>Applies <see cref="AddAsync"/>'s semantics per item, sequentially and non-atomically.</remarks>
    public async Task AddRangeAsync(IEnumerable<TAggregate> aggregates, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(aggregates);
        foreach (var aggregate in aggregates)
        {
            ct.ThrowIfCancellationRequested();
            await AddAsync(aggregate, ct).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">Thrown when <see cref="SimulateFailure"/> is <see langword="true"/>.</exception>
    /// <exception cref="System.Collections.Generic.KeyNotFoundException">Thrown when no aggregate with the derived key exists.</exception>
    public Task UpdateAsync(TAggregate aggregate, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        ct.ThrowIfCancellationRequested();
        ThrowIfSimulatingFailure();

        var key = _idSelector(aggregate);
        if (!_items.ContainsKey(key))
            throw new System.Collections.Generic.KeyNotFoundException($"No aggregate with key '{key}' exists to update.");

        _items[key] = aggregate;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    /// <remarks>Applies <see cref="UpdateAsync"/>'s semantics per item, sequentially and non-atomically.</remarks>
    public async Task UpdateRangeAsync(IEnumerable<TAggregate> aggregates, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(aggregates);
        foreach (var aggregate in aggregates)
        {
            ct.ThrowIfCancellationRequested();
            await UpdateAsync(aggregate, ct).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// ALWAYS a hard removal, even when <typeparamref name="TAggregate"/> implements
    /// <see cref="ISoftDeletable"/> — this fake never flips <c>IsDeleted</c> in place, since that
    /// property has <c>private set</c> in production and is populated exclusively via EF Core's
    /// <c>ChangeTracker</c>. Idempotent — a missing key is a silent no-op.
    /// </remarks>
    /// <exception cref="InvalidOperationException">Thrown when <see cref="SimulateFailure"/> is <see langword="true"/>.</exception>
    public Task DeleteAsync(TAggregate aggregate, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        ct.ThrowIfCancellationRequested();
        ThrowIfSimulatingFailure();

        _items.TryRemove(_idSelector(aggregate), out _);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    /// <remarks>Applies <see cref="DeleteAsync"/>'s semantics per item, sequentially and non-atomically.</remarks>
    public async Task DeleteRangeAsync(IEnumerable<TAggregate> aggregates, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(aggregates);
        foreach (var aggregate in aggregates)
        {
            ct.ThrowIfCancellationRequested();
            await DeleteAsync(aggregate, ct).ConfigureAwait(false);
        }
    }

    // ----- IReadRepository<TAggregate, TId> (read side) -----

    /// <inheritdoc />
    public Task<IReadOnlyList<TAggregate>> ListAsync(ISpecification<TAggregate> spec, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ct.ThrowIfCancellationRequested();
        IReadOnlyList<TAggregate> result = ApplySpecification(spec).ToList();
        return Task.FromResult(result);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Reuses the IDENTICAL pipeline <see cref="ListAsync"/> uses, including <c>Skip</c>/<c>Take</c>
    /// if set on <paramref name="spec"/> — no special-casing, mirroring
    /// <c>ISpecificationEvaluator&lt;T&gt;.GetQuery</c>'s own method-agnostic real behavior.
    /// </remarks>
    public Task<int> CountAsync(ISpecification<TAggregate> spec, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(ApplySpecification(spec).Count());
    }

    /// <inheritdoc />
    /// <remarks>Reuses the identical pipeline <see cref="ListAsync"/> uses — see <see cref="CountAsync"/>.</remarks>
    public Task<bool> AnyAsync(ISpecification<TAggregate> spec, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(ApplySpecification(spec).Any());
    }

    /// <inheritdoc />
    /// <remarks>
    /// Direct per-id dictionary lookups — never soft-delete-filtered, mirroring production's own
    /// raw-<c>Contains</c>-predicate shape (which likewise bypasses the specification pipeline). A
    /// missing id produces no entry; result order is not guaranteed.
    /// </remarks>
    public Task<IReadOnlyList<TAggregate>> GetByIdsAsync(IEnumerable<TId> ids, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ids);
        ct.ThrowIfCancellationRequested();

        var result = new List<TAggregate>();
        foreach (var id in ids)
        {
            if (_items.TryGetValue(id, out var value))
                result.Add(value);
        }

        return Task.FromResult<IReadOnlyList<TAggregate>>(result);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Produces IDENTICAL results to <see cref="GetByIdsAsync"/>, chunked mechanically into groups of
    /// <paramref name="chunkSize"/> — a purely additive, opt-in sibling.
    /// </remarks>
    public async Task<IReadOnlyList<TAggregate>> GetByIdsChunkedAsync(
        IEnumerable<TId> ids,
        int chunkSize,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ids);
        if (chunkSize < 1)
            throw new ArgumentOutOfRangeException(nameof(chunkSize), chunkSize, "chunkSize must be at least 1.");

        ct.ThrowIfCancellationRequested();

        var result = new List<TAggregate>();
        foreach (var chunk in ids.Chunk(chunkSize))
        {
            ct.ThrowIfCancellationRequested();
            var chunkResult = await GetByIdsAsync(chunk, ct).ConfigureAwait(false);
            result.AddRange(chunkResult);
        }

        return result;
    }

    /// <inheritdoc />
    /// <remarks>
    /// <c>page</c>/<c>pageSize</c> are derived from <paramref name="spec"/>'s <c>Skip</c>/<c>Take</c>
    /// using the inverse of <c>PagedSpecification&lt;T&gt;</c>'s own construction arithmetic.
    /// <c>totalCount</c> comes from a second, un-paged pass through the filter/order/distinct steps
    /// only (steps 0-1/3-5) — never Skip/Take.
    /// </remarks>
    public Task<PagedList<TAggregate>> ListPagedAsync(ISpecification<TAggregate> spec, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ct.ThrowIfCancellationRequested();

        var totalCount = ApplyFilterOrderDistinct(spec).Count();
        var (page, pageSize) = DerivePaging(spec, totalCount);
        var items = ApplySpecification(spec).ToList();

        return Task.FromResult(PagedList<TAggregate>.Create(items, page, pageSize, totalCount));
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<TResult>> ListProjectedAsync<TResult>(
        IProjectionSpecification<TAggregate, TResult> spec,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ct.ThrowIfCancellationRequested();

        var selector = spec.Selector.Compile();
        IReadOnlyList<TResult> result = ApplySpecification(spec).Select(selector).ToList();
        return Task.FromResult(result);
    }

    /// <inheritdoc />
    public Task<TResult?> GetBySpecProjectedAsync<TResult>(
        IProjectionSpecification<TAggregate, TResult> spec,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ct.ThrowIfCancellationRequested();

        var selector = spec.Selector.Compile();
        return Task.FromResult(ApplySpecification(spec).Select(selector).FirstOrDefault());
    }

    /// <inheritdoc />
    /// <remarks>Same page/pageSize/totalCount derivation as <see cref="ListPagedAsync"/>, with the projection applied last.</remarks>
    public Task<PagedList<TResult>> ListPagedProjectedAsync<TResult>(
        IProjectionSpecification<TAggregate, TResult> spec,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ct.ThrowIfCancellationRequested();

        var totalCount = ApplyFilterOrderDistinct(spec).Count();
        var (page, pageSize) = DerivePaging(spec, totalCount);
        var selector = spec.Selector.Compile();
        var items = ApplySpecification(spec).Select(selector).ToList();

        return Task.FromResult(PagedList<TResult>.Create(items, page, pageSize, totalCount));
    }

    /// <inheritdoc />
    /// <remarks>
    /// A genuine cancellable async iterator — the result is eagerly materialized first (there is no
    /// lazy DB source to stream from), then yielded lazily with a per-item cancellation check,
    /// mirroring <c>Storage/InMemoryFileStorage.ListAsync</c>'s established shape.
    /// </remarks>
    public async IAsyncEnumerable<TAggregate> StreamAsync(
        ISpecification<TAggregate> spec,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(spec);

        var items = ApplySpecification(spec).ToList();
        foreach (var item in items)
        {
            ct.ThrowIfCancellationRequested();
            yield return item;
            await Task.Yield();
        }
    }

    /// <inheritdoc />
    /// <remarks>Same shape as <see cref="StreamAsync"/>, with the projection applied before yielding.</remarks>
    public async IAsyncEnumerable<TResult> StreamProjectedAsync<TResult>(
        IProjectionSpecification<TAggregate, TResult> spec,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(spec);

        var selector = spec.Selector.Compile();
        var items = ApplySpecification(spec).Select(selector).ToList();
        foreach (var item in items)
        {
            ct.ThrowIfCancellationRequested();
            yield return item;
            await Task.Yield();
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// Applies the filter/order/distinct steps (0-1, 3-5), then a cursor seek: when
    /// <c>spec.AfterKey</c> is <see langword="null"/> (first page), every sorted item is kept;
    /// otherwise, items at-or-before the cursor are skipped via a comparison that mirrors the
    /// ACTUAL composed sort order <see cref="KeysetSpecification{T, TKey}"/> produces (primary key
    /// per <c>spec.Descending</c>, id tiebreak always ascending, per the base type's own
    /// <c>ApplyThenBy(idSelector, descending: false)</c>).
    /// </para>
    /// <para>
    /// Over-fetches by one row (<c>Take + 1</c>) to compute <c>HasMore</c> without a second round
    /// trip; the extra lookahead row is trimmed before <c>NextAfterKey</c>/<c>NextAfterId</c> are
    /// derived from the trimmed page's own last item. Assumes the id shape implements
    /// <see cref="IComparable"/> (true for every <typeparamref name="TId"/> shape used on this
    /// platform — <see cref="Guid"/>/<see cref="int"/>/<see cref="long"/>/<see cref="string"/>).
    /// </para>
    /// </remarks>
    public Task<KeysetPage<TAggregate, TKey>> ListKeysetAsync<TKey>(
        KeysetSpecification<TAggregate, TKey> spec,
        CancellationToken ct = default)
        where TKey : struct, IComparable<TKey>
    {
        ArgumentNullException.ThrowIfNull(spec);
        ct.ThrowIfCancellationRequested();

        var sorted = ApplyFilterOrderDistinct(spec).ToList();

        var keySelector = (spec.OrderBy ?? spec.OrderByDescending)!.Compile();
        var idSelector = spec.ThenBys[0].KeySelector.Compile();

        IEnumerable<TAggregate> afterCursor = sorted;
        if (spec.AfterKey is { } afterKey)
        {
            var afterId = spec.AfterId;
            afterCursor = sorted.SkipWhile(item =>
            {
                var itemKey = (TKey)keySelector(item);
                var primaryRank = itemKey.CompareTo(afterKey);
                if (spec.Descending)
                    primaryRank = -primaryRank;

                if (primaryRank != 0)
                    return primaryRank < 0;

                // Primary keys equal — break the tie via id. KeysetSpecification<T,TKey> always
                // applies the id ThenBy ascending (descending: false), regardless of the primary
                // sort direction, so the tiebreak comparison never flips.
                return Comparer<object>.Default.Compare(idSelector(item), afterId) <= 0;
            });
        }

        var take = spec.Take!.Value;
        var page = afterCursor.Take(take + 1).ToList();

        var hasMore = page.Count > take;
        if (hasMore)
            page.RemoveAt(page.Count - 1);

        TKey? nextAfterKey = null;
        object? nextAfterId = null;
        if (hasMore && page.Count > 0)
        {
            var lastItem = page[^1];
            nextAfterKey = (TKey)keySelector(lastItem);
            nextAfterId = idSelector(lastItem);
        }

        return Task.FromResult(new KeysetPage<TAggregate, TKey>(page, nextAfterKey, nextAfterId, hasMore));
    }

    // ----- Test-setup / introspection -----

    /// <summary>
    /// Bulk-populates the backing store via the constructor's <c>idSelector</c>, bypassing
    /// <see cref="SimulateFailure"/> and the <see cref="AddAsync"/>-throws-on-duplicate guard. Test
    /// SETUP, not code under test — overwrites an existing entry sharing the same derived key.
    /// </summary>
    /// <param name="aggregates">The aggregates to seed.</param>
    public void Seed(IEnumerable<TAggregate> aggregates)
    {
        ArgumentNullException.ThrowIfNull(aggregates);
        foreach (var aggregate in aggregates)
            _items[_idSelector(aggregate)] = aggregate;
    }

    /// <summary>
    /// Clears the backing store only. Does NOT reset <see cref="SimulateFailure"/> — a test
    /// controlling both independently is never surprised by an implicit reset of one.
    /// </summary>
    public void Reset() => _items.Clear();

    // ----- Shared in-memory specification-evaluation pipeline -----

    private void ThrowIfSimulatingFailure()
    {
        if (SimulateFailure)
            throw new InvalidOperationException("FakeRepository was configured to simulate failure.");
    }

    /// <summary>
    /// Applies steps 0-1 (soft-delete filter, criteria), 3-4 (ordering), and 5 (distinct) of the
    /// canonical specification-evaluator pipeline — everything except paging (step 7). Shared by
    /// every read-side member, including the total-count pass for paged queries.
    /// </summary>
    private IEnumerable<TAggregate> ApplyFilterOrderDistinct(ISpecification<TAggregate> spec)
    {
        IEnumerable<TAggregate> query = _items.Values;

        // Step 0: soft-delete filter — a runtime `is` check, never a generic constraint.
        if (!spec.IncludeDeleted)
            query = query.Where(item => item is not ISoftDeletable { IsDeleted: true });

        // Step 1: criteria — null Criteria matches all entities.
        if (spec.Criteria is not null)
            query = query.Where(spec.Criteria.Compile());

        // Steps 2/2b/2c (Includes/StringIncludes/AsSplitQuery) are deliberate in-memory no-ops —
        // there is no ORM query plan or Cartesian-join concept; seeded aggregates already carry
        // whatever graph the test constructed.

        // Steps 3-4: primary ordering, then secondary ThenBys — ThenBys are ignored without a
        // primary sort, matching well-behaved repository semantics documented on ISpecification<T>.
        query = ApplyOrdering(query, spec);

        // Step 5: distinct.
        if (spec.IsDistinct)
            query = query.Distinct();

        // Step 6 (AsNoTracking) is a deliberate in-memory no-op — there is no ChangeTracker concept.

        return query;
    }

    private static IEnumerable<TAggregate> ApplyOrdering(IEnumerable<TAggregate> query, ISpecification<TAggregate> spec)
    {
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

    /// <summary>
    /// Applies the full pipeline (<see cref="ApplyFilterOrderDistinct"/> plus step 7, Skip/Take —
    /// ALWAYS last). Used by every member except <see cref="ListKeysetAsync{TKey}"/>, which applies
    /// its own seek-based paging instead of Skip/Take.
    /// </summary>
    private IEnumerable<TAggregate> ApplySpecification(ISpecification<TAggregate> spec)
    {
        var query = ApplyFilterOrderDistinct(spec);

        if (spec.Skip is { } skip)
            query = query.Skip(skip);

        if (spec.Take is { } take)
            query = query.Take(take);

        return query;
    }

    private static (int Page, int PageSize) DerivePaging(ISpecification<TAggregate> spec, int totalCount)
    {
        // Falls back to a single full page when the caller passes a plain, non-paged specification
        // (spec.Take is null). Math.Max(totalCount, 1) guards PagedList<T>.Create's own
        // pageSize-must-be-at-least-1 requirement for an empty, unpaged result set.
        var pageSize = spec.Take ?? Math.Max(totalCount, 1);
        var page = spec.Skip.HasValue && pageSize > 0 ? (spec.Skip.Value / pageSize) + 1 : 1;
        return (page, pageSize);
    }
}

using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Contracts.Pagination;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Domain.Specifications;
using SharedKernel.Persistence.Abstractions.Repositories;
using SharedKernel.Persistence.Abstractions.Specifications;
using SharedKernel.Persistence.EfCore.Specifications;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.EfCore.Diagnostics;
using SharedKernel.Persistence.EfCore.ReadReplica;

namespace SharedKernel.Persistence.EfCore.Repositories;

/// <summary>
/// Abstract EF Core implementation of the read-side repository contract.
/// Uses <see cref="ISpecificationEvaluator{T}"/> internally — never exposes <see cref="IQueryable{T}"/>.
/// </summary>
/// <typeparam name="TAggregate">
/// The aggregate root type. Must implement <see cref="IAggregateRoot{TId}"/>.
/// </typeparam>
/// <typeparam name="TId">The aggregate's identity type. Must be non-null.</typeparam>
/// <remarks>
/// <para>
/// Concrete read repositories extend this class and are registered as
/// <c>IReadRepository&lt;TAggregate, TId&gt;</c> in DI.
/// </para>
/// <para>
/// Pass <c>ReadOnlySpecification&lt;T&gt;</c> subclasses (which set
/// <c>AsNoTracking = true</c>) on read-heavy paths to avoid change-tracking overhead.
/// </para>
/// <para>
/// <strong>Breaking change:</strong> <c>GetByIdAsync</c> has been removed.
/// Use <c>GetBySpecAsync(new ByIdSpecification&lt;TAggregate, TId&gt;(id), cancellationToken)</c> instead.
/// </para>
/// <para>
/// <strong>Observability:</strong> every public method on this class except
/// <see cref="GetByIdsChunkedAsync"/> is wrapped in a distributed-tracing span via
/// <see cref="SharedKernel.Persistence.EfCore.Diagnostics.RepositoryTracing"/>, emitted on
/// <see cref="SharedKernel.Persistence.EfCore.Diagnostics.PersistenceActivitySource"/>
/// and tagged with <see cref="SharedKernel.Persistence.EfCore.Diagnostics.PersistenceTagKeys"/>.
/// <see cref="StreamAsync"/>/<see cref="StreamProjectedAsync{TResult}"/> spans wrap the FULL
/// enumeration — started before the first yield, ended after the last.
/// <see cref="GetByIdsChunkedAsync"/> produces its own traced spans only indirectly, one per
/// underlying <see cref="GetByIdsAsync"/> call it issues, rather than a single span of its own.
/// </para>
/// <para>
/// <strong>Keyset guard:</strong> <see cref="GetBySpecAsync"/>, <see cref="ListAsync"/>,
/// <see cref="CountAsync"/>, and <see cref="AnyAsync"/> all reject a
/// <see cref="KeysetSpecification{T, TKey}"/> outright rather than silently ignoring its cursor and
/// returning the first page — see <see cref="ListKeysetAsync{TKey}"/>.
/// </para>
/// </remarks>
public abstract class EfReadRepository<TAggregate, TId> : IReadRepository<TAggregate, TId>
    where TAggregate : class, IAggregateRoot<TId>
    where TId : notnull
{
    /// <summary>The underlying EF Core context.</summary>
    protected SharedKernelDbContext DbContext { get; }

    private readonly ISpecificationEvaluator<TAggregate> _evaluator;
    private readonly IReadReplicaContextAccessor<SharedKernelDbContext>? _replicaAccessor;

    /// <summary>
    /// Initialises a new <see cref="EfReadRepository{TAggregate, TId}"/>.
    /// </summary>
    /// <param name="dbContext">The scoped shared-kernel DB context.</param>
    /// <param name="evaluator">
    /// The specification evaluator that translates <see cref="ISpecification{T}"/> into
    /// a composable <see cref="IQueryable{T}"/> pipeline. Any <see cref="ISpecificationEvaluator{T}"/>
    /// implementation works — no downcast to the concrete <c>SpecificationEvaluator&lt;T&gt;</c>
    /// type is performed.
    /// </param>
    /// <param name="replicaAccessor">
    /// Optional read-replica routing accessor. Resolved by DI only when
    /// <c>EfCorePersistenceBuilder{TContext}.WithReadReplica(...)</c> was called — otherwise
    /// <see langword="null"/>, in which case every read method targets <see cref="DbContext"/>
    /// directly, exactly as before this parameter existed. Purely additive — every existing
    /// <see cref="EfReadRepository{TAggregate, TId}"/> subclass continues to compile and behave
    /// identically without passing anything new.
    /// READ-AFTER-WRITE CONSISTENCY BECOMES THE CALLER'S RESPONSIBILITY ONCE THIS IS NON-NULL — a
    /// handler that writes then immediately reads through this repository in the same logical
    /// operation MAY OBSERVE STALE DATA under replication lag. A read issued while an EF Core
    /// transaction is active on <see cref="DbContext"/> is NEVER routed to the replica.
    /// </param>
    protected EfReadRepository(
        SharedKernelDbContext dbContext,
        ISpecificationEvaluator<TAggregate> evaluator,
        IReadReplicaContextAccessor<SharedKernelDbContext>? replicaAccessor = null)
    {
        DbContext = dbContext;
        _evaluator = evaluator;
        _replicaAccessor = replicaAccessor;
    }

    /// <summary>
    /// The context every read method executes against — <see cref="DbContext"/> itself, or a
    /// read-replica context when read-replica routing is enabled and no transaction is active.
    /// </summary>
    /// <remarks>
    /// Resolved AFRESH on every access, never cached at the property-read call site — transaction
    /// state can legitimately change between two read calls issued against the same injected
    /// repository instance.
    /// </remarks>
    private SharedKernelDbContext EffectiveContext =>
        _replicaAccessor?.GetEffectiveContext(DbContext) ?? DbContext;

    /// <inheritdoc />
    public virtual Task<TAggregate?> GetBySpecAsync(
        ISpecification<TAggregate> spec,
        CancellationToken cancellationToken = default)
        => RepositoryTracing.ExecuteTracedAsync<TAggregate, TAggregate?>(nameof(GetBySpecAsync), async () =>
        {
            KeysetSpecificationGuard.EnsureNotKeyset(spec, nameof(GetBySpecAsync));
            var query = _evaluator.GetQuery(EffectiveContext.Set<TAggregate>(), spec);
            return await query.FirstOrDefaultAsync(cancellationToken);
        });

    /// <inheritdoc />
    public virtual Task<IReadOnlyList<TAggregate>> ListAsync(
        ISpecification<TAggregate> spec,
        CancellationToken cancellationToken = default)
        => RepositoryTracing.ExecuteTracedAsync<TAggregate, IReadOnlyList<TAggregate>>(nameof(ListAsync), async () =>
        {
            KeysetSpecificationGuard.EnsureNotKeyset(spec, nameof(ListAsync));
            var query = _evaluator.GetQuery(EffectiveContext.Set<TAggregate>(), spec);
            return await query.ToListAsync(cancellationToken);
        });

    /// <inheritdoc />
    public virtual Task<long> CountAsync(
        ISpecification<TAggregate> spec,
        CancellationToken cancellationToken = default)
        => RepositoryTracing.ExecuteTracedAsync<TAggregate, long>(nameof(CountAsync), async () =>
        {
            KeysetSpecificationGuard.EnsureNotKeyset(spec, nameof(CountAsync));
            // A count must always be the count of every matching row, never a
            // page of it — strip Skip/Take exactly like ListPagedAsync's own count query does.
            var query = StripPaging(_evaluator.GetQuery(EffectiveContext.Set<TAggregate>(), spec), spec);
            return await query.LongCountAsync(cancellationToken);
        });

    /// <inheritdoc />
    public virtual Task<bool> AnyAsync(
        ISpecification<TAggregate> spec,
        CancellationToken cancellationToken = default)
        => RepositoryTracing.ExecuteTracedAsync<TAggregate, bool>(nameof(AnyAsync), async () =>
        {
            KeysetSpecificationGuard.EnsureNotKeyset(spec, nameof(AnyAsync));
            var query = _evaluator.GetQuery(EffectiveContext.Set<TAggregate>(), spec);
            return await query.AnyAsync(cancellationToken);
        });

    /// <inheritdoc />
    /// <remarks>
    /// Uses a plain, compile-time-typed <c>e =&gt; idList.Contains(e.Id)</c> predicate — no
    /// longer built via <c>Type.GetMethods()</c> + <c>MakeGenericMethod</c>, since
    /// <typeparamref name="TId"/> is already a compile-time generic parameter of this class) so that
    /// EF Core's registered <see cref="Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter"/>
    /// is applied at the property level by the LINQ provider, generating a server-side
    /// <c>WHERE "Id" IN (...)</c> clause.
    /// </remarks>
    public virtual Task<IReadOnlyList<TAggregate>> GetByIdsAsync(
        IEnumerable<TId> ids,
        CancellationToken cancellationToken = default)
        => RepositoryTracing.ExecuteTracedAsync<TAggregate, IReadOnlyList<TAggregate>>(
            nameof(GetByIdsAsync), async () =>
        {
            var idList = ids as List<TId> ?? ids.ToList();

            return await EffectiveContext.Set<TAggregate>()
                .Where(e => idList.Contains(e.Id))
                .ToListAsync(cancellationToken);
        });

    /// <inheritdoc />
    /// <remarks>
    /// Issues <c>ceil(N / chunkSize)</c> sequential <see cref="GetByIdsAsync"/>-shaped
    /// round trips and concatenates the results. A purely additive, opt-in sibling — does not alter
    /// <see cref="GetByIdsAsync"/>'s own single-query behavior in any way.
    /// </remarks>
    public virtual async Task<IReadOnlyList<TAggregate>> GetByIdsChunkedAsync(
        IEnumerable<TId> ids,
        int chunkSize,
        CancellationToken cancellationToken = default)
    {
        if (chunkSize < 1)
            throw new ArgumentOutOfRangeException(nameof(chunkSize), chunkSize,
                "Chunk size must be greater than or equal to 1.");

        var idList = ids as IReadOnlyList<TId> ?? ids.ToList();
        if (idList.Count == 0)
            return [];

        var results = new List<TAggregate>(idList.Count);

        for (var offset = 0; offset < idList.Count; offset += chunkSize)
        {
            var chunk = idList.Skip(offset).Take(chunkSize);
            results.AddRange(await GetByIdsAsync(chunk, cancellationToken));
        }

        return results;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Issues two database round-trips under the same <see cref="SharedKernelDbContext"/> scope:
    /// <list type="number">
    /// <item><description>
    /// <strong>Count query:</strong> the specification is evaluated without Skip/Take (via
    /// <c>NoPagingWrapper</c>) and <c>LongCountAsync</c> is called to obtain the true total.
    /// </description></item>
    /// <item><description>
    /// <strong>Data query:</strong> the full specification (including Skip/Take) is evaluated
    /// and <c>ToListAsync</c> is called to obtain the current page of items.
    /// </description></item>
    /// </list>
    /// Both queries share the same connection and transaction scope.
    /// <c>PagedList&lt;T&gt;</c> is defined in <c>SharedKernel.Contracts</c> (04.Contracts).
    /// </remarks>
    public virtual Task<PagedList<TAggregate>> ListPagedAsync(
        ISpecification<TAggregate> spec,
        CancellationToken cancellationToken = default)
        => RepositoryTracing.ExecuteTracedAsync<TAggregate, PagedList<TAggregate>>(nameof(ListPagedAsync), async () =>
        {
            KeysetSpecificationGuard.EnsureNotKeyset(spec, nameof(ListPagedAsync));

            // Count query: apply spec without Skip/Take to get the true total.
            var countQuery = _evaluator.GetQuery(EffectiveContext.Set<TAggregate>(), spec);
            // Strip paging from count — we need the full-filter count.
            var totalCount = await StripPaging(countQuery, spec).LongCountAsync(cancellationToken);

            // Data query: full spec including Skip/Take.
            var dataQuery = _evaluator.GetQuery(EffectiveContext.Set<TAggregate>(), spec);
            var items = await dataQuery.ToListAsync(cancellationToken);

            return CreatePage(items, spec, totalCount);
        });

    /// <inheritdoc />
    /// <remarks>
    /// Delegates to <see cref="ISpecificationEvaluator{T}.GetProjectedQuery{TResult}"/> which
    /// runs the full aggregate pipeline (criteria, includes, ordering, Skip/Take) and then applies
    /// <c>.Select(spec.Selector)</c> as the final step. Any <see cref="ISpecificationEvaluator{T}"/>
    /// implementation works — no downcast to the concrete type is performed.
    /// </remarks>
    public virtual Task<IReadOnlyList<TResult>> ListProjectedAsync<TResult>(
        IProjectionSpecification<TAggregate, TResult> spec,
        CancellationToken cancellationToken = default)
        => RepositoryTracing.ExecuteTracedAsync<TAggregate, IReadOnlyList<TResult>>(
            nameof(ListProjectedAsync), async () =>
        {
            var projected = _evaluator.GetProjectedQuery(EffectiveContext.Set<TAggregate>(), spec);
            return await projected.ToListAsync(cancellationToken);
        });

    /// <inheritdoc />
    /// <remarks>
    /// Delegates to <see cref="ISpecificationEvaluator{T}.GetProjectedQuery{TResult}"/> and calls
    /// <c>FirstOrDefaultAsync</c> on the resulting projected queryable. The
    /// <c>Select(spec.Selector)</c> is applied after all Skip/Take operations, consistent with the
    /// paging-last invariant. Any <see cref="ISpecificationEvaluator{T}"/> implementation works —
    /// no downcast performed.
    /// </remarks>
    public virtual Task<TResult?> GetBySpecProjectedAsync<TResult>(
        IProjectionSpecification<TAggregate, TResult> spec,
        CancellationToken cancellationToken = default)
        => RepositoryTracing.ExecuteTracedAsync<TAggregate, TResult?>(
            nameof(GetBySpecProjectedAsync), async () =>
        {
            var projected = _evaluator.GetProjectedQuery(EffectiveContext.Set<TAggregate>(), spec);
            return await projected.FirstOrDefaultAsync(cancellationToken);
        });

    /// <inheritdoc />
    /// <remarks>
    /// Issues two database round-trips under the same <see cref="SharedKernelDbContext"/> scope:
    /// <list type="number">
    /// <item><description>
    /// <strong>Count query:</strong> the specification is evaluated without projection and
    /// without Skip/Take (via <c>NoPagingWrapper</c>), and <c>LongCountAsync</c> is called to
    /// obtain the true total.
    /// </description></item>
    /// <item><description>
    /// <strong>Data query:</strong> the full specification (including projection and Skip/Take)
    /// is evaluated via <c>GetProjectedQuery</c>, and <c>ToListAsync</c> is called to obtain
    /// the current page of projected items.
    /// </description></item>
    /// </list>
    /// Both queries share the same connection and transaction scope.
    /// <c>PagedList&lt;T&gt;</c> is defined in <c>SharedKernel.Contracts</c> (04.Contracts).
    /// </remarks>
    public virtual Task<PagedList<TResult>> ListPagedProjectedAsync<TResult>(
        IProjectionSpecification<TAggregate, TResult> spec,
        CancellationToken cancellationToken = default)
        => RepositoryTracing.ExecuteTracedAsync<TAggregate, PagedList<TResult>>(
            nameof(ListPagedProjectedAsync), async () =>
        {
            // Count query: apply spec without projection and without Skip/Take.
            var countSpec = new NoPagingWrapper<TAggregate>(spec);
            var totalCount = await _evaluator.GetQuery(EffectiveContext.Set<TAggregate>(), countSpec).LongCountAsync(cancellationToken);

            // Data query: full spec including projection and Skip/Take.
            var projected = _evaluator.GetProjectedQuery(EffectiveContext.Set<TAggregate>(), spec);
            var items = await projected.ToListAsync(cancellationToken);

            return CreatePage(items, spec, totalCount);
        });

    /// <inheritdoc />
    /// <remarks>
    /// Built on <see cref="ISpecificationEvaluator{T}.GetQuery"/> followed by an unconditional
    /// <c>AsNoTracking()</c> and <c>AsAsyncEnumerable()</c> — the one documented exception to
    /// "the spec's <see cref="ISpecification{T}.AsNoTracking"/> flag is honored", because a
    /// long-lived streaming enumeration under change tracking would grow the change tracker
    /// unbounded for the lifetime of the enumeration. <see cref="ISpecification{T}.Skip"/> and
    /// <see cref="ISpecification{T}.Take"/>, if set, are applied as a normal row-window by the
    /// evaluator before the query is converted to <see cref="IAsyncEnumerable{T}"/>.
    /// </remarks>
    public virtual IAsyncEnumerable<TAggregate> StreamAsync(
        ISpecification<TAggregate> spec,
        CancellationToken cancellationToken = default)
    {
        var query = _evaluator.GetQuery(EffectiveContext.Set<TAggregate>(), spec).AsNoTracking();
        return RepositoryTracing.ExecuteTracedStreamAsync<TAggregate, TAggregate>(
            nameof(StreamAsync), query.AsAsyncEnumerable(), cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Built on <see cref="ISpecificationEvaluator{T}.GetProjectedQuery{TResult}"/> followed by an
    /// unconditional <c>AsNoTracking()</c> and <c>AsAsyncEnumerable()</c> — see
    /// <see cref="StreamAsync"/> for rationale. Any <see cref="ISpecificationEvaluator{T}"/>
    /// implementation works — no downcast to the concrete type is performed.
    /// </remarks>
    public virtual IAsyncEnumerable<TResult> StreamProjectedAsync<TResult>(
        IProjectionSpecification<TAggregate, TResult> spec,
        CancellationToken cancellationToken = default)
    {
        var query = _evaluator.GetProjectedQuery(EffectiveContext.Set<TAggregate>().AsNoTracking(), spec);
        return RepositoryTracing.ExecuteTracedStreamAsync<TAggregate, TResult>(
            nameof(StreamProjectedAsync), query.AsAsyncEnumerable(), cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Calls <see cref="ISpecificationEvaluator{T}.GetKeysetQuery{TKey}"/>
    /// (which fetches <c>spec.Take + 1</c> rows — see that method's step-7 deviation), computes
    /// <c>HasMore</c> from whether the extra probe row was returned, trims to the declared page
    /// size, then builds the opaque <c>NextCursor</c> from the LAST row of the trimmed page by
    /// compiling <c>spec.OrderBy</c>/<c>spec.OrderByDescending</c> and
    /// <c>spec.ThenBys[0].KeySelector</c> to <see cref="Func{TAggregate, TResult}"/> delegates
    /// (<c>.Compile()</c>, once per call — the same accepted expression-compilation cost class as
    /// <c>Specification&lt;T&gt;.IsSatisfiedBy</c>) and encoding the result via
    /// <see cref="PageCursor.Encode{TKey, TId}"/>.
    /// </remarks>
    public virtual Task<CursorPagedList<TAggregate>> ListKeysetAsync<TKey>(
        KeysetSpecification<TAggregate, TKey> spec,
        CancellationToken cancellationToken = default)
        where TKey : struct, IComparable<TKey>
        => RepositoryTracing.ExecuteTracedAsync<TAggregate, CursorPagedList<TAggregate>>(
            nameof(ListKeysetAsync), async () =>
        {
            var query = _evaluator.GetKeysetQuery(EffectiveContext.Set<TAggregate>(), spec);
            var rows = await query.ToListAsync(cancellationToken);

            return CursorPagedList<TAggregate>.FromLookahead(
                rows, spec.Take!.Value, last => BuildCursor(spec, last));
        });

    /// <inheritdoc />
    /// <remarks>
    /// The projected sibling of <see cref="ListKeysetAsync{TKey}"/> — the cursor is always
    /// built from the un-projected row (the sort key and identity are read off
    /// <typeparamref name="TAggregate"/>, never <typeparamref name="TResult"/>), then
    /// <paramref name="selector"/> is applied after paging, consistent with the paging-last invariant
    /// every other projected read method on this class follows.
    /// </remarks>
    public virtual Task<CursorPagedList<TResult>> ListKeysetProjectedAsync<TKey, TResult>(
        KeysetSpecification<TAggregate, TKey> spec,
        Expression<Func<TAggregate, TResult>> selector,
        CancellationToken cancellationToken = default)
        where TKey : struct, IComparable<TKey>
        => RepositoryTracing.ExecuteTracedAsync<TAggregate, CursorPagedList<TResult>>(
            nameof(ListKeysetProjectedAsync), async () =>
        {
            var query = _evaluator.GetKeysetQuery(EffectiveContext.Set<TAggregate>(), spec);
            var rows = await query.ToListAsync(cancellationToken);

            var compiledSelector = selector.Compile();
            var page = CursorPagedList<TAggregate>.FromLookahead(
                rows, spec.Take!.Value, last => BuildCursor(spec, last));

            return page.Map(compiledSelector);
        });

    // Builds the opaque next-page cursor from the last kept row of a keyset page, using the same
    // sort-key/identity selectors GetKeysetQuery's own seek predicate is built from.
    private static string BuildCursor<TKey>(KeysetSpecification<TAggregate, TKey> spec, TAggregate last)
        where TKey : struct, IComparable<TKey>
    {
        var keySelector = (spec.Descending ? spec.OrderByDescending! : spec.OrderBy!).Compile();
        var idSelector = spec.ThenBys[0].KeySelector.Compile();

        var key = (TKey)keySelector(last);
        var id = idSelector(last);

        return PageCursor.Encode(key, id);
    }

    // Strips Skip/Take from the already-evaluated query for count purposes.
    // We re-apply criteria/includes/ordering without paging by applying GetQuery on a fresh set
    // then ignoring the Skip/Take from the returned query. Since EfCore's IQueryable composes
    // lazily this is done by building a count-specific query from the base set.
    private IQueryable<TAggregate> StripPaging(
        IQueryable<TAggregate> evaluatedQuery,
        ISpecification<TAggregate> spec)
    {
        // If the spec has no paging, evaluatedQuery is already correct for counting.
        if (!spec.Skip.HasValue && !spec.Take.HasValue)
            return evaluatedQuery;

        // Re-build the query without the paging step by applying spec criteria/includes/ordering
        // from scratch. We use a NoPagingWrapper to provide all spec properties except Skip/Take.
        var countSpec = new NoPagingWrapper<TAggregate>(spec);
        return _evaluator.GetQuery(EffectiveContext.Set<TAggregate>(), countSpec);
    }

    // Builds the page from the rows the data query returned. The page size is the Take the evaluator
    // applied, so a page can hold more rows than its size only when a custom evaluator ignores Take;
    // that is reported as an evaluator bug rather than returned as an inconsistent page.
    private static PagedList<TItem> CreatePage<TItem>(
        List<TItem> items,
        ISpecification<TAggregate> spec,
        long totalCount)
    {
        var (page, pageSize) = ExtractPageInfo(spec, items.Count);

        if (items.Count > pageSize)
        {
            throw new InvalidOperationException(
                $"The specification evaluator returned {items.Count} rows for a page of size {pageSize}. " +
                "An ISpecificationEvaluator must apply the specification's Take as the page window.");
        }

        return PagedList<TItem>.Create(items, page, pageSize, totalCount);
    }

    // Derives the 1-based page number and page size from the paging the specification applies.
    // A PagedSpecification<T> supplies its own Page/PageSize when they still match its Skip/Take (a
    // subclass may have replaced them with ApplyPaging). Otherwise the page is inferred from Skip/Take,
    // rounding a Skip that is not a multiple of Take down to the page it starts in. A specification with
    // no Take returns every matching row, so the whole result is reported as a single page.
    private static (int Page, int PageSize) ExtractPageInfo(ISpecification<TAggregate> spec, int itemCount)
    {
        if (spec is PagedSpecification<TAggregate> paged
            && spec.Take == paged.PageSize
            && spec.Skip == ((long)paged.Page - 1) * paged.PageSize)
        {
            return (paged.Page, paged.PageSize);
        }

        if (spec.Take is { } take)
            return (((spec.Skip ?? 0) / take) + 1, take);

        return (1, Math.Max(itemCount, 1));
    }

    // Wraps an existing ISpecification<T> and zeroes out Skip/Take for count queries.
    private sealed class NoPagingWrapper<T> : ISpecification<T>
    {
        private readonly ISpecification<T> _inner;

        internal NoPagingWrapper(ISpecification<T> inner) => _inner = inner;

        public Expression<Func<T, bool>>? Criteria => _inner.Criteria;
        public IReadOnlyList<Expression<Func<T, object>>> Includes => _inner.Includes;
        public Expression<Func<T, object>>? OrderBy => _inner.OrderBy;
        public Expression<Func<T, object>>? OrderByDescending => _inner.OrderByDescending;
        public IReadOnlyList<(Expression<Func<T, object>> KeySelector, bool Descending)> ThenBys => _inner.ThenBys;
        public int? Skip => null;
        public int? Take => null;
        public bool IsDistinct => _inner.IsDistinct;
        public bool AsNoTracking => _inner.AsNoTracking;
        public bool AsSplitQuery => _inner.AsSplitQuery;
        public bool IncludeDeleted => _inner.IncludeDeleted;
        public IReadOnlyList<string> StringIncludes => _inner.StringIncludes;
    }
}

using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Contracts.Pagination;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Domain.Specifications;
using SharedKernel.Persistence.Abstractions.Repositories;
using SharedKernel.Persistence.Abstractions.Specifications;
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
/// <strong>Breaking change (P-080):</strong> <c>GetByIdAsync</c> has been removed.
/// Use <c>GetBySpecAsync(new ByIdSpecification&lt;TAggregate, TId&gt;(id), ct)</c> instead.
/// </para>
/// <para>
/// <strong>Observability (WO-051/P-319):</strong> every public method on this class except
/// <see cref="GetByIdsChunkedAsync"/> is wrapped in a distributed-tracing span via
/// <see cref="SharedKernel.Persistence.EfCore.Diagnostics.RepositoryTracing"/>, emitted on
/// <see cref="SharedKernel.Persistence.EfCore.Diagnostics.PersistenceActivitySource"/>
/// (<c>"SharedKernel.Persistence"</c>/<c>"1.0"</c>) and tagged with
/// <see cref="SharedKernel.Persistence.EfCore.Diagnostics.PersistenceTagKeys"/>.
/// <see cref="StreamAsync"/>/<see cref="StreamProjectedAsync{TResult}"/> spans wrap the FULL
/// enumeration — started before the first yield, ended after the last.
/// <see cref="GetByIdsChunkedAsync"/> produces its own traced spans only indirectly, one per
/// underlying <see cref="GetByIdsAsync"/> call it issues, rather than a single span of its own.
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
    /// type is performed (P-097).
    /// </param>
    /// <param name="replicaAccessor">
    /// Optional read-replica routing accessor (WO-053/P-338). Resolved by DI only when
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
    /// repository instance (WO-053/P-338).
    /// </remarks>
    private SharedKernelDbContext EffectiveContext =>
        _replicaAccessor?.GetEffectiveContext(DbContext) ?? DbContext;

    /// <inheritdoc />
    public virtual Task<TAggregate?> GetBySpecAsync(
        ISpecification<TAggregate> spec,
        CancellationToken ct = default)
        => RepositoryTracing.ExecuteTracedAsync<TAggregate, TAggregate?>(nameof(GetBySpecAsync), async () =>
        {
            var query = _evaluator.GetQuery(EffectiveContext.Set<TAggregate>(), spec);
            return await query.FirstOrDefaultAsync(ct);
        });

    /// <inheritdoc />
    public virtual Task<IReadOnlyList<TAggregate>> ListAsync(
        ISpecification<TAggregate> spec,
        CancellationToken ct = default)
        => RepositoryTracing.ExecuteTracedAsync<TAggregate, IReadOnlyList<TAggregate>>(nameof(ListAsync), async () =>
        {
            var query = _evaluator.GetQuery(EffectiveContext.Set<TAggregate>(), spec);
            return await query.ToListAsync(ct);
        });

    /// <inheritdoc />
    public virtual Task<int> CountAsync(
        ISpecification<TAggregate> spec,
        CancellationToken ct = default)
        => RepositoryTracing.ExecuteTracedAsync<TAggregate, int>(nameof(CountAsync), async () =>
        {
            var query = _evaluator.GetQuery(EffectiveContext.Set<TAggregate>(), spec);
            return await query.CountAsync(ct);
        });

    /// <inheritdoc />
    public virtual Task<bool> AnyAsync(
        ISpecification<TAggregate> spec,
        CancellationToken ct = default)
        => RepositoryTracing.ExecuteTracedAsync<TAggregate, bool>(nameof(AnyAsync), async () =>
        {
            var query = _evaluator.GetQuery(EffectiveContext.Set<TAggregate>(), spec);
            return await query.AnyAsync(ct);
        });

    /// <inheritdoc />
    /// <remarks>
    /// Uses an expression-tree <c>Contains</c> predicate (<c>e => ids.Contains(e.Id)</c>) so that
    /// EF Core's registered <see cref="Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter"/>
    /// is applied at the property level by the LINQ provider, generating a server-side
    /// <c>WHERE "Id" IN (...)</c> clause. The prior <c>EF.Property</c> approach has been removed
    /// because it could silently fall back to client-side evaluation when <typeparamref name="TId"/>
    /// is a strongly-typed ID with a registered converter (P-105 Fix 2).
    /// </remarks>
    public virtual Task<IReadOnlyList<TAggregate>> GetByIdsAsync(
        IEnumerable<TId> ids,
        CancellationToken ct = default)
        => RepositoryTracing.ExecuteTracedAsync<TAggregate, IReadOnlyList<TAggregate>>(
            nameof(GetByIdsAsync), async () =>
        {
            var idList = ids as List<TId> ?? ids.ToList();

            // Build expression tree: e => idList.Contains(e.Id)
            // This ensures the registered ValueConverter is applied at the property level by the
            // LINQ provider, generating a server-side WHERE Id IN (...) clause.
            var param = Expression.Parameter(typeof(TAggregate), "e");
            var idProperty = Expression.Property(param, "Id");
            var idListConstant = Expression.Constant(idList);

            // Enumerable.Contains<TId>(IEnumerable<TId>, TId)
            var containsMethod = typeof(Enumerable)
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .First(m => m.Name == nameof(Enumerable.Contains) && m.GetParameters().Length == 2)
                .MakeGenericMethod(typeof(TId));

            var containsCall = Expression.Call(containsMethod, idListConstant, idProperty);
            var predicate = Expression.Lambda<Func<TAggregate, bool>>(containsCall, param);

            return await EffectiveContext.Set<TAggregate>()
                .Where(predicate)
                .ToListAsync(ct);
        });

    /// <inheritdoc />
    /// <remarks>
    /// WO-051/P-323 — issues <c>ceil(N / chunkSize)</c> sequential <see cref="GetByIdsAsync"/>-shaped
    /// round trips and concatenates the results. A purely additive, opt-in sibling — does not alter
    /// <see cref="GetByIdsAsync"/>'s own single-query behavior in any way.
    /// </remarks>
    public virtual async Task<IReadOnlyList<TAggregate>> GetByIdsChunkedAsync(
        IEnumerable<TId> ids,
        int chunkSize,
        CancellationToken ct = default)
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
            results.AddRange(await GetByIdsAsync(chunk, ct));
        }

        return results;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Issues two database round-trips under the same <see cref="SharedKernelDbContext"/> scope:
    /// <list type="number">
    ///   <item><description>
    ///     <strong>Count query:</strong> the specification is evaluated without Skip/Take (via
    ///     <c>NoPagingWrapper</c>) and <c>CountAsync</c> is called to obtain the true total.
    ///   </description></item>
    ///   <item><description>
    ///     <strong>Data query:</strong> the full specification (including Skip/Take) is evaluated
    ///     and <c>ToListAsync</c> is called to obtain the current page of items.
    ///   </description></item>
    /// </list>
    /// Both queries share the same connection and transaction scope.
    /// <c>PagedList&lt;T&gt;</c> is defined in <c>SharedKernel.Contracts</c> (04.Contracts).
    /// </remarks>
    public virtual Task<PagedList<TAggregate>> ListPagedAsync(
        ISpecification<TAggregate> spec,
        CancellationToken ct = default)
        => RepositoryTracing.ExecuteTracedAsync<TAggregate, PagedList<TAggregate>>(nameof(ListPagedAsync), async () =>
        {
            // Count query: apply spec without Skip/Take to get the true total.
            var countQuery = _evaluator.GetQuery(EffectiveContext.Set<TAggregate>(), spec);
            // Strip paging from count — we need the full-filter count.
            var totalCount = await StripPaging(countQuery, spec).CountAsync(ct);

            // Data query: full spec including Skip/Take.
            var dataQuery = _evaluator.GetQuery(EffectiveContext.Set<TAggregate>(), spec);
            var items = await dataQuery.ToListAsync(ct);

            // Extract page metadata from spec (PagedSpecification carries these).
            var (page, pageSize) = ExtractPageInfo(spec);

            return PagedList<TAggregate>.Create(items, page, pageSize, totalCount);
        });

    /// <inheritdoc />
    /// <remarks>
    /// Delegates to <see cref="ISpecificationEvaluator{T}.GetProjectedQuery{TResult}"/> which
    /// runs the full aggregate pipeline (criteria, includes, ordering, Skip/Take) and then applies
    /// <c>.Select(spec.Selector)</c> as the final step. Any <see cref="ISpecificationEvaluator{T}"/>
    /// implementation works — no downcast to the concrete type is performed (P-097).
    /// </remarks>
    public virtual Task<IReadOnlyList<TResult>> ListProjectedAsync<TResult>(
        IProjectionSpecification<TAggregate, TResult> spec,
        CancellationToken ct = default)
        => RepositoryTracing.ExecuteTracedAsync<TAggregate, IReadOnlyList<TResult>>(
            nameof(ListProjectedAsync), async () =>
        {
            var projected = _evaluator.GetProjectedQuery(EffectiveContext.Set<TAggregate>(), spec);
            return await projected.ToListAsync(ct);
        });

    /// <inheritdoc />
    /// <remarks>
    /// Delegates to <see cref="ISpecificationEvaluator{T}.GetProjectedQuery{TResult}"/> and calls
    /// <c>FirstOrDefaultAsync</c> on the resulting projected queryable. The
    /// <c>Select(spec.Selector)</c> is applied after all Skip/Take operations, consistent with the
    /// paging-last invariant. Any <see cref="ISpecificationEvaluator{T}"/> implementation works —
    /// no downcast performed (P-097).
    /// </remarks>
    public virtual Task<TResult?> GetBySpecProjectedAsync<TResult>(
        IProjectionSpecification<TAggregate, TResult> spec,
        CancellationToken ct = default)
        => RepositoryTracing.ExecuteTracedAsync<TAggregate, TResult?>(
            nameof(GetBySpecProjectedAsync), async () =>
        {
            var projected = _evaluator.GetProjectedQuery(EffectiveContext.Set<TAggregate>(), spec);
            return await projected.FirstOrDefaultAsync(ct);
        });

    /// <inheritdoc />
    /// <remarks>
    /// Issues two database round-trips under the same <see cref="SharedKernelDbContext"/> scope:
    /// <list type="number">
    ///   <item><description>
    ///     <strong>Count query:</strong> the specification is evaluated without projection and
    ///     without Skip/Take (via <c>NoPagingWrapper</c>), and <c>CountAsync</c> is called to
    ///     obtain the true total.
    ///   </description></item>
    ///   <item><description>
    ///     <strong>Data query:</strong> the full specification (including projection and Skip/Take)
    ///     is evaluated via <c>GetProjectedQuery</c>, and <c>ToListAsync</c> is called to obtain
    ///     the current page of projected items.
    ///   </description></item>
    /// </list>
    /// Both queries share the same connection and transaction scope.
    /// <c>PagedList&lt;T&gt;</c> is defined in <c>SharedKernel.Contracts</c> (04.Contracts).
    /// </remarks>
    public virtual Task<PagedList<TResult>> ListPagedProjectedAsync<TResult>(
        IProjectionSpecification<TAggregate, TResult> spec,
        CancellationToken ct = default)
        => RepositoryTracing.ExecuteTracedAsync<TAggregate, PagedList<TResult>>(
            nameof(ListPagedProjectedAsync), async () =>
        {
            // Count query: apply spec without projection and without Skip/Take.
            var countSpec = new NoPagingWrapper<TAggregate>(spec);
            var totalCount = await _evaluator.GetQuery(EffectiveContext.Set<TAggregate>(), countSpec).CountAsync(ct);

            // Data query: full spec including projection and Skip/Take.
            var projected = _evaluator.GetProjectedQuery(EffectiveContext.Set<TAggregate>(), spec);
            var items = await projected.ToListAsync(ct);

            var (page, pageSize) = ExtractPageInfo(spec);
            return PagedList<TResult>.Create(items, page, pageSize, totalCount);
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
        CancellationToken ct = default)
    {
        var query = _evaluator.GetQuery(EffectiveContext.Set<TAggregate>(), spec).AsNoTracking();
        return RepositoryTracing.ExecuteTracedStreamAsync<TAggregate, TAggregate>(
            nameof(StreamAsync), query.AsAsyncEnumerable(), ct);
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
        CancellationToken ct = default)
    {
        var query = _evaluator.GetProjectedQuery(EffectiveContext.Set<TAggregate>().AsNoTracking(), spec);
        return RepositoryTracing.ExecuteTracedStreamAsync<TAggregate, TResult>(
            nameof(StreamProjectedAsync), query.AsAsyncEnumerable(), ct);
    }

    /// <inheritdoc />
    /// <remarks>
    /// WO-051/P-317 — calls <see cref="ISpecificationEvaluator{T}.GetKeysetQuery{TKey}"/> (which
    /// fetches <c>spec.Take + 1</c> rows — see that method's step-7 deviation), computes
    /// <c>HasMore</c> from whether the extra probe row was returned, trims to the declared page
    /// size, then computes <c>NextAfterKey</c>/<c>NextAfterId</c> from the LAST row of the trimmed
    /// page by compiling <c>spec.OrderBy</c>/<c>spec.OrderByDescending</c> and
    /// <c>spec.ThenBys[0].KeySelector</c> to <see cref="Func{TAggregate, TResult}"/> delegates
    /// (<c>.Compile()</c>, once per call — the same accepted expression-compilation cost class as
    /// <c>Specification&lt;T&gt;.IsSatisfiedBy</c>) and invoking them against the last item, casting
    /// the boxed key result to <typeparamref name="TKey"/> (safe — <typeparamref name="TKey"/> is
    /// statically known at this call site, zero reflection).
    /// </remarks>
    public virtual Task<KeysetPage<TAggregate, TKey>> ListKeysetAsync<TKey>(
        KeysetSpecification<TAggregate, TKey> spec,
        CancellationToken ct = default)
        where TKey : struct, IComparable<TKey>
        => RepositoryTracing.ExecuteTracedAsync<TAggregate, KeysetPage<TAggregate, TKey>>(
            nameof(ListKeysetAsync), async () =>
        {
            var query = _evaluator.GetKeysetQuery(EffectiveContext.Set<TAggregate>(), spec);
            var rows = await query.ToListAsync(ct);

            var hasMore = rows.Count > spec.Take!.Value;
            var page = hasMore ? rows.Take(spec.Take.Value).ToList() : rows;

            TKey? nextAfterKey = null;
            object? nextAfterId = null;

            if (hasMore && page.Count > 0)
            {
                var last = page[^1];
                var keySelector = (spec.Descending ? spec.OrderByDescending! : spec.OrderBy!).Compile();
                var idSelector = spec.ThenBys[0].KeySelector.Compile();

                nextAfterKey = (TKey)keySelector(last);
                nextAfterId = idSelector(last);
            }

            return new KeysetPage<TAggregate, TKey>(page, nextAfterKey, nextAfterId, hasMore);
        });

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

    // Extracts page and pageSize from a spec that implements PagedSpecification<T>.
    // Falls back to sensible defaults when the spec is not a PagedSpecification.
    private static (int page, int pageSize) ExtractPageInfo(ISpecification<TAggregate> spec)
    {
        if (spec is PagedSpecification<TAggregate> paged)
            return (paged.Page, paged.PageSize);

        // For non-paged specs, infer from Skip/Take or use defaults.
        var skip = spec.Skip ?? 0;
        var take = spec.Take ?? int.MaxValue;
        var pageSize = take == int.MaxValue ? 1 : take;
        var page = take == int.MaxValue ? 1 : (skip / take) + 1;
        return (page, pageSize);
    }

    // Wraps an existing ISpecification<T> and zeroes out Skip/Take for count queries.
    private sealed class NoPagingWrapper<T> : ISpecification<T>
    {
        private readonly ISpecification<T> _inner;

        internal NoPagingWrapper(ISpecification<T> inner) => _inner = inner;

        public System.Linq.Expressions.Expression<Func<T, bool>>? Criteria => _inner.Criteria;
        public IReadOnlyList<System.Linq.Expressions.Expression<Func<T, object>>> Includes => _inner.Includes;
        public System.Linq.Expressions.Expression<Func<T, object>>? OrderBy => _inner.OrderBy;
        public System.Linq.Expressions.Expression<Func<T, object>>? OrderByDescending => _inner.OrderByDescending;
        public IReadOnlyList<(System.Linq.Expressions.Expression<Func<T, object>> KeySelector, bool Descending)> ThenBys => _inner.ThenBys;
        public int? Skip => null;
        public int? Take => null;
        public bool IsDistinct => _inner.IsDistinct;
        public bool AsNoTracking => _inner.AsNoTracking;
        public bool AsSplitQuery => _inner.AsSplitQuery;
        public bool IncludeDeleted => _inner.IncludeDeleted;
        public IReadOnlyList<string> StringIncludes => _inner.StringIncludes;
    }
}

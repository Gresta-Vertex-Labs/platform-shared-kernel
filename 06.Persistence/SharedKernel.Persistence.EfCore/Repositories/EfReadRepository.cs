using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Contracts.Pagination;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Domain.Specifications;
using SharedKernel.Persistence.Abstractions.Repositories;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.EfCore.Diagnostics;
using SharedKernel.Persistence.EfCore.Specifications;

namespace SharedKernel.Persistence.EfCore.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IReadRepository{TAggregate, TId}"/>: every query runs without change
/// tracking.
/// </summary>
/// <typeparam name="TAggregate">The aggregate root type.</typeparam>
/// <typeparam name="TId">The aggregate's identity type.</typeparam>
/// <remarks>
/// <para>
/// <b>No class needed.</b> <c>IReadRepository&lt;TAggregate, TId&gt;</c> is registered automatically for every
/// aggregate the service's DbContexts map. Derive from this class only to add custom queries (and register the
/// subclass yourself), or to override <see cref="AggregateQuery"/>.
/// </para>
/// <para>
/// <b>Observability.</b> Every method runs in a <c>"{Aggregate}.{Method}"</c> span on the
/// <c>SharedKernel.Persistence</c> activity source; each query is tagged with its specification's type name.
/// </para>
/// </remarks>
public class EfReadRepository<TAggregate, TId> : IReadRepository<TAggregate, TId>
    where TAggregate : class, IAggregateRoot<TId>
    where TId : notnull
{
    /// <summary>Initializes a new read repository over <paramref name="dbContext"/>.</summary>
    /// <param name="dbContext">The context that maps <typeparamref name="TAggregate"/>.</param>
    /// <param name="evaluator">The specification evaluator; <see cref="SpecificationEvaluator{T}"/> when <see langword="null"/>.</param>
    public EfReadRepository(SharedKernelDbContext dbContext, ISpecificationEvaluator<TAggregate>? evaluator = null)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        DbContext = dbContext;
        Evaluator = evaluator ?? new SpecificationEvaluator<TAggregate>();
    }

    /// <summary>Gets the context the repository queries.</summary>
    protected SharedKernelDbContext DbContext { get; }

    /// <summary>Gets the specification evaluator.</summary>
    protected ISpecificationEvaluator<TAggregate> Evaluator { get; }

    /// <summary>
    /// Returns the query that loads a <em>complete</em> aggregate, used by <c>GetByIdAsync</c> and
    /// <c>GetByIdsAsync</c>.
    /// </summary>
    /// <returns><c>DbContext.Set&lt;TAggregate&gt;()</c> by default.</returns>
    /// <remarks>
    /// Navigations configured with <c>Navigation(...).AutoInclude()</c> are loaded automatically; that is the
    /// preferred way to define the aggregate boundary. Override this hook to add includes the model cannot
    /// express, for example <c>=&gt; base.AggregateQuery().Include(o =&gt; o.Lines).ThenInclude(l =&gt; l.Discounts)</c>.
    /// Do not apply tracking or filters here.
    /// </remarks>
    protected virtual IQueryable<TAggregate> AggregateQuery() => DbContext.Set<TAggregate>();

    /// <summary>Applies <paramref name="spec"/> to the aggregate set, without tracking behaviour.</summary>
    /// <param name="spec">The specification.</param>
    /// <returns>The composed query.</returns>
    protected IQueryable<TAggregate> Query(ISpecification<TAggregate> spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        return Evaluator.GetQuery(DbContext.Set<TAggregate>(), spec);
    }

    /// <inheritdoc />
    public virtual Task<TAggregate?> GetByIdAsync(TId id, CancellationToken cancellationToken = default) =>
        RepositoryTracing.ExecuteTracedAsync<TAggregate, TAggregate?>(nameof(GetByIdAsync), () =>
            AggregateQuery().AsNoTracking()
                .Where(RepositoryExpressions<TAggregate, TId>.ById(id))
                .FirstOrDefaultAsync(cancellationToken));

    /// <inheritdoc />
    public virtual Task<IReadOnlyList<TAggregate>> GetByIdsAsync(IEnumerable<TId> ids, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ids);

        return RepositoryTracing.ExecuteTracedAsync<TAggregate, IReadOnlyList<TAggregate>>(nameof(GetByIdsAsync), async () =>
        {
            var idList = ids as List<TId> ?? ids.ToList();
            if (idList.Count == 0)
                return [];

            return await AggregateQuery().AsNoTracking()
                .Where(e => idList.Contains(e.Id))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
        });
    }

    /// <inheritdoc />
    public virtual Task<bool> ExistsAsync(TId id, CancellationToken cancellationToken = default) =>
        RepositoryTracing.ExecuteTracedAsync<TAggregate, bool>(nameof(ExistsAsync), () =>
            DbContext.Set<TAggregate>().AnyAsync(RepositoryExpressions<TAggregate, TId>.ById(id), cancellationToken));

    /// <inheritdoc />
    public virtual Task<TAggregate?> FirstOrDefaultAsync(ISpecification<TAggregate> spec, CancellationToken cancellationToken = default) =>
        RepositoryTracing.ExecuteTracedAsync<TAggregate, TAggregate?>(nameof(FirstOrDefaultAsync), () =>
            Query(spec).AsNoTracking().FirstOrDefaultAsync(cancellationToken));

    /// <inheritdoc />
    public virtual Task<IReadOnlyList<TAggregate>> ListAsync(ISpecification<TAggregate> spec, CancellationToken cancellationToken = default) =>
        RepositoryTracing.ExecuteTracedAsync<TAggregate, IReadOnlyList<TAggregate>>(nameof(ListAsync), async () =>
            await Query(spec).AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false));

    /// <inheritdoc />
    public virtual Task<long> CountAsync(ISpecification<TAggregate> spec, CancellationToken cancellationToken = default) =>
        RepositoryTracing.ExecuteTracedAsync<TAggregate, long>(nameof(CountAsync), () =>
            Query(new UnpagedSpecification<TAggregate>(spec)).LongCountAsync(cancellationToken));

    /// <inheritdoc />
    public virtual Task<bool> AnyAsync(ISpecification<TAggregate> spec, CancellationToken cancellationToken = default) =>
        RepositoryTracing.ExecuteTracedAsync<TAggregate, bool>(nameof(AnyAsync), () =>
            Query(spec).AnyAsync(cancellationToken));

    /// <inheritdoc />
    public virtual Task<PagedList<TAggregate>> ListPagedAsync(
        ISpecification<TAggregate> spec,
        PageRequest page,
        CancellationToken cancellationToken = default)
    {
        PagingGuard.EnsureOffsetPageable(spec, nameof(ListPagedAsync));
        ArgumentNullException.ThrowIfNull(page);

        return RepositoryTracing.ExecuteTracedAsync<TAggregate, PagedList<TAggregate>>(nameof(ListPagedAsync), async () =>
        {
            var total = await Query(spec).LongCountAsync(cancellationToken).ConfigureAwait(false);
            var items = total <= page.Offset
                ? []
                : await Query(spec).AsNoTracking().Skip(page.Offset).Take(page.PageSize)
                    .ToListAsync(cancellationToken).ConfigureAwait(false);

            return PagedList<TAggregate>.Create(items, page, total);
        });
    }

    /// <inheritdoc />
    public virtual Task<CursorPagedList<TAggregate>> ListKeysetAsync<TKey>(
        ISpecification<TAggregate> spec,
        CursorPageRequest page,
        Expression<Func<TAggregate, TKey>> keySelector,
        bool descending = false,
        CancellationToken cancellationToken = default)
        where TKey : notnull
    {
        PagingGuard.EnsureKeysetPageable(spec, nameof(ListKeysetAsync));
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(keySelector);

        var keyAccessor = RepositoryExpressions<TAggregate, TId>.KeyAccessor(keySelector);
        var after = KeysetCursor.Decode<TKey, TId>(page);

        return RepositoryTracing.ExecuteTracedAsync<TAggregate, CursorPagedList<TAggregate>>(nameof(ListKeysetAsync), async () =>
        {
            var rows = await Query(spec).AsNoTracking()
                .ToKeysetPage(keySelector, RepositoryExpressions<TAggregate, TId>.IdSelector, after, descending, page.Limit)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            return CursorPagedList<TAggregate>.FromLookahead(
                rows, page.Limit, last => KeysetCursor.Encode(keyAccessor(last), last.Id));
        });
    }

    /// <inheritdoc />
    public virtual IAsyncEnumerable<TAggregate> StreamAsync(ISpecification<TAggregate> spec, CancellationToken cancellationToken = default) =>
        RepositoryTracing.ExecuteTracedStreamAsync<TAggregate, TAggregate>(
            nameof(StreamAsync), Query(spec).AsNoTracking().AsAsyncEnumerable(), cancellationToken);

    /// <inheritdoc />
    public virtual Task<TResult?> FirstOrDefaultProjectedAsync<TResult>(
        IProjectionSpecification<TAggregate, TResult> spec,
        CancellationToken cancellationToken = default) =>
        RepositoryTracing.ExecuteTracedAsync<TAggregate, TResult?>(nameof(FirstOrDefaultProjectedAsync), () =>
            Evaluator.GetProjectedQuery(DbContext.Set<TAggregate>().AsNoTracking(), spec).FirstOrDefaultAsync(cancellationToken));

    /// <inheritdoc />
    public virtual Task<IReadOnlyList<TResult>> ListProjectedAsync<TResult>(
        IProjectionSpecification<TAggregate, TResult> spec,
        CancellationToken cancellationToken = default) =>
        RepositoryTracing.ExecuteTracedAsync<TAggregate, IReadOnlyList<TResult>>(nameof(ListProjectedAsync), async () =>
            await Evaluator.GetProjectedQuery(DbContext.Set<TAggregate>().AsNoTracking(), spec)
                .ToListAsync(cancellationToken).ConfigureAwait(false));

    /// <inheritdoc />
    public virtual Task<PagedList<TResult>> ListPagedProjectedAsync<TResult>(
        IProjectionSpecification<TAggregate, TResult> spec,
        PageRequest page,
        CancellationToken cancellationToken = default)
    {
        PagingGuard.EnsureOffsetPageable(spec, nameof(ListPagedProjectedAsync));
        ArgumentNullException.ThrowIfNull(page);

        return RepositoryTracing.ExecuteTracedAsync<TAggregate, PagedList<TResult>>(nameof(ListPagedProjectedAsync), async () =>
        {
            var total = await Query(spec).LongCountAsync(cancellationToken).ConfigureAwait(false);
            var items = total <= page.Offset
                ? []
                : await Query(spec).AsNoTracking().Skip(page.Offset).Take(page.PageSize).Select(spec.Selector)
                    .ToListAsync(cancellationToken).ConfigureAwait(false);

            return PagedList<TResult>.Create(items, page, total);
        });
    }

    /// <inheritdoc />
    public virtual Task<CursorPagedList<TResult>> ListKeysetProjectedAsync<TKey, TResult>(
        IProjectionSpecification<TAggregate, TResult> spec,
        CursorPageRequest page,
        Expression<Func<TAggregate, TKey>> keySelector,
        bool descending = false,
        CancellationToken cancellationToken = default)
        where TKey : notnull
    {
        PagingGuard.EnsureKeysetPageable(spec, nameof(ListKeysetProjectedAsync));
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(keySelector);

        var after = KeysetCursor.Decode<TKey, TId>(page);
        var rowSelector = BuildKeysetRowSelector(spec.Selector, keySelector);

        return RepositoryTracing.ExecuteTracedAsync<TAggregate, CursorPagedList<TResult>>(nameof(ListKeysetProjectedAsync), async () =>
        {
            // Server-side projection: the item, the sort key and the identity come back in one SELECT, so the
            // cursor needs no in-memory accessor and no compiled delegate (A27).
            var rows = await Query(spec).AsNoTracking()
                .ToKeysetPage(keySelector, RepositoryExpressions<TAggregate, TId>.IdSelector, after, descending, page.Limit)
                .Select(rowSelector)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var cursorPage = CursorPagedList<KeysetRow<TResult, TKey, TId>>.FromLookahead(
                rows, page.Limit, last => KeysetCursor.Encode(last.Key, last.Id));

            return cursorPage.Map(row => row.Item);
        });
    }

    /// <inheritdoc />
    public virtual IAsyncEnumerable<TResult> StreamProjectedAsync<TResult>(
        IProjectionSpecification<TAggregate, TResult> spec,
        CancellationToken cancellationToken = default) =>
        RepositoryTracing.ExecuteTracedStreamAsync<TAggregate, TResult>(
            nameof(StreamProjectedAsync),
            Evaluator.GetProjectedQuery(DbContext.Set<TAggregate>().AsNoTracking(), spec).AsAsyncEnumerable(),
            cancellationToken);

    // e => new KeysetRow { Item = selector(e), Key = key(e), Id = e.Id }, over one parameter.
    private static Expression<Func<TAggregate, KeysetRow<TResult, TKey, TId>>> BuildKeysetRowSelector<TResult, TKey>(
        Expression<Func<TAggregate, TResult>> selector,
        Expression<Func<TAggregate, TKey>> keySelector)
    {
        var parameter = selector.Parameters[0];
        var key = new ReplaceParameter(keySelector.Parameters[0], parameter).Visit(keySelector.Body)!;
        var idSelector = RepositoryExpressions<TAggregate, TId>.IdSelector;
        var id = new ReplaceParameter(idSelector.Parameters[0], parameter).Visit(idSelector.Body)!;

        var rowType = typeof(KeysetRow<TResult, TKey, TId>);
        var body = Expression.MemberInit(
            Expression.New(rowType),
            Expression.Bind(rowType.GetProperty(nameof(KeysetRow<TResult, TKey, TId>.Item))!, selector.Body),
            Expression.Bind(rowType.GetProperty(nameof(KeysetRow<TResult, TKey, TId>.Key))!, key),
            Expression.Bind(rowType.GetProperty(nameof(KeysetRow<TResult, TKey, TId>.Id))!, id));

        return Expression.Lambda<Func<TAggregate, KeysetRow<TResult, TKey, TId>>>(body, parameter);
    }

    private sealed class ReplaceParameter(ParameterExpression source, ParameterExpression target) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) =>
            node == source ? target : base.VisitParameter(node);
    }
}

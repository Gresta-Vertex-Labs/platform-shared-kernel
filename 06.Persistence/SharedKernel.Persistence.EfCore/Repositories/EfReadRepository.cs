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
/// </remarks>
public abstract class EfReadRepository<TAggregate, TId> : IReadRepository<TAggregate, TId>
    where TAggregate : class, IAggregateRoot<TId>
    where TId : notnull
{
    /// <summary>The underlying EF Core context.</summary>
    protected SharedKernelDbContext DbContext { get; }

    private readonly ISpecificationEvaluator<TAggregate> _evaluator;

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
    protected EfReadRepository(
        SharedKernelDbContext dbContext,
        ISpecificationEvaluator<TAggregate> evaluator)
    {
        DbContext = dbContext;
        _evaluator = evaluator;
    }

    /// <inheritdoc />
    public virtual async Task<TAggregate?> GetBySpecAsync(
        ISpecification<TAggregate> spec,
        CancellationToken ct = default)
    {
        var query = _evaluator.GetQuery(DbContext.Set<TAggregate>(), spec);
        return await query.FirstOrDefaultAsync(ct);
    }

    /// <inheritdoc />
    public virtual async Task<IReadOnlyList<TAggregate>> ListAsync(
        ISpecification<TAggregate> spec,
        CancellationToken ct = default)
    {
        var query = _evaluator.GetQuery(DbContext.Set<TAggregate>(), spec);
        return await query.ToListAsync(ct);
    }

    /// <inheritdoc />
    public virtual async Task<int> CountAsync(
        ISpecification<TAggregate> spec,
        CancellationToken ct = default)
    {
        var query = _evaluator.GetQuery(DbContext.Set<TAggregate>(), spec);
        return await query.CountAsync(ct);
    }

    /// <inheritdoc />
    public virtual async Task<bool> AnyAsync(
        ISpecification<TAggregate> spec,
        CancellationToken ct = default)
    {
        var query = _evaluator.GetQuery(DbContext.Set<TAggregate>(), spec);
        return await query.AnyAsync(ct);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Uses an expression-tree <c>Contains</c> predicate (<c>e => ids.Contains(e.Id)</c>) so that
    /// EF Core's registered <see cref="Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter"/>
    /// is applied at the property level by the LINQ provider, generating a server-side
    /// <c>WHERE "Id" IN (...)</c> clause. The prior <c>EF.Property</c> approach has been removed
    /// because it could silently fall back to client-side evaluation when <typeparamref name="TId"/>
    /// is a strongly-typed ID with a registered converter (P-105 Fix 2).
    /// </remarks>
    public virtual async Task<IReadOnlyList<TAggregate>> GetByIdsAsync(
        IEnumerable<TId> ids,
        CancellationToken ct = default)
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

        return await DbContext.Set<TAggregate>()
            .Where(predicate)
            .ToListAsync(ct);
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
    public virtual async Task<PagedList<TAggregate>> ListPagedAsync(
        ISpecification<TAggregate> spec,
        CancellationToken ct = default)
    {
        // Count query: apply spec without Skip/Take to get the true total.
        var countQuery = _evaluator.GetQuery(DbContext.Set<TAggregate>(), spec);
        // Strip paging from count — we need the full-filter count.
        var totalCount = await StripPaging(countQuery, spec).CountAsync(ct);

        // Data query: full spec including Skip/Take.
        var dataQuery = _evaluator.GetQuery(DbContext.Set<TAggregate>(), spec);
        var items = await dataQuery.ToListAsync(ct);

        // Extract page metadata from spec (PagedSpecification carries these).
        var (page, pageSize) = ExtractPageInfo(spec);

        return PagedList<TAggregate>.Create(items, page, pageSize, totalCount);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Delegates to <see cref="ISpecificationEvaluator{T}.GetProjectedQuery{TResult}"/> which
    /// runs the full aggregate pipeline (criteria, includes, ordering, Skip/Take) and then applies
    /// <c>.Select(spec.Selector)</c> as the final step. Any <see cref="ISpecificationEvaluator{T}"/>
    /// implementation works — no downcast to the concrete type is performed (P-097).
    /// </remarks>
    public virtual async Task<IReadOnlyList<TResult>> ListProjectedAsync<TResult>(
        IProjectionSpecification<TAggregate, TResult> spec,
        CancellationToken ct = default)
    {
        var projected = _evaluator.GetProjectedQuery(DbContext.Set<TAggregate>(), spec);
        return await projected.ToListAsync(ct);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Delegates to <see cref="ISpecificationEvaluator{T}.GetProjectedQuery{TResult}"/> and calls
    /// <c>FirstOrDefaultAsync</c> on the resulting projected queryable. The
    /// <c>Select(spec.Selector)</c> is applied after all Skip/Take operations, consistent with the
    /// paging-last invariant. Any <see cref="ISpecificationEvaluator{T}"/> implementation works —
    /// no downcast performed (P-097).
    /// </remarks>
    public virtual async Task<TResult?> GetBySpecProjectedAsync<TResult>(
        IProjectionSpecification<TAggregate, TResult> spec,
        CancellationToken ct = default)
    {
        var projected = _evaluator.GetProjectedQuery(DbContext.Set<TAggregate>(), spec);
        return await projected.FirstOrDefaultAsync(ct);
    }

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
    public virtual async Task<PagedList<TResult>> ListPagedProjectedAsync<TResult>(
        IProjectionSpecification<TAggregate, TResult> spec,
        CancellationToken ct = default)
    {
        // Count query: apply spec without projection and without Skip/Take.
        var countSpec = new NoPagingWrapper<TAggregate>(spec);
        var totalCount = await _evaluator.GetQuery(DbContext.Set<TAggregate>(), countSpec).CountAsync(ct);

        // Data query: full spec including projection and Skip/Take.
        var projected = _evaluator.GetProjectedQuery(DbContext.Set<TAggregate>(), spec);
        var items = await projected.ToListAsync(ct);

        var (page, pageSize) = ExtractPageInfo(spec);
        return PagedList<TResult>.Create(items, page, pageSize, totalCount);
    }

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
    public virtual async IAsyncEnumerable<TAggregate> StreamAsync(
        ISpecification<TAggregate> spec,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var query = _evaluator.GetQuery(DbContext.Set<TAggregate>(), spec).AsNoTracking();

        await foreach (var item in query.AsAsyncEnumerable().WithCancellation(ct))
            yield return item;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Built on <see cref="ISpecificationEvaluator{T}.GetProjectedQuery{TResult}"/> followed by an
    /// unconditional <c>AsNoTracking()</c> and <c>AsAsyncEnumerable()</c> — see
    /// <see cref="StreamAsync"/> for rationale. Any <see cref="ISpecificationEvaluator{T}"/>
    /// implementation works — no downcast to the concrete type is performed.
    /// </remarks>
    public virtual async IAsyncEnumerable<TResult> StreamProjectedAsync<TResult>(
        IProjectionSpecification<TAggregate, TResult> spec,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var query = _evaluator.GetProjectedQuery(DbContext.Set<TAggregate>().AsNoTracking(), spec);

        await foreach (var item in query.AsAsyncEnumerable().WithCancellation(ct))
            yield return item;
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
        return _evaluator.GetQuery(DbContext.Set<TAggregate>(), countSpec);
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
        public bool IncludeDeleted => _inner.IncludeDeleted;
        public IReadOnlyList<string> StringIncludes => _inner.StringIncludes;
    }
}

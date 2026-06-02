using Microsoft.EntityFrameworkCore;
using SharedKernel.Contracts.Pagination;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Domain.Specifications;
using SharedKernel.Persistence.Abstractions.Repositories;
using SharedKernel.Persistence.Abstractions.Specifications;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.EfCore.Specifications;

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

    private readonly SpecificationEvaluator<TAggregate> _evaluator;

    /// <summary>
    /// Initialises a new <see cref="EfReadRepository{TAggregate, TId}"/>.
    /// </summary>
    /// <param name="dbContext">The scoped shared-kernel DB context.</param>
    /// <param name="evaluator">
    /// The specification evaluator that translates <see cref="ISpecification{T}"/> into
    /// a composable <see cref="IQueryable{T}"/> pipeline.
    /// </param>
    protected EfReadRepository(
        SharedKernelDbContext dbContext,
        ISpecificationEvaluator<TAggregate> evaluator)
    {
        DbContext = dbContext;
        // Downcast to the concrete evaluator so GetProjectedQuery is accessible without
        // changing the ISpecificationEvaluator<T> interface (which remains ORM-free in Abstractions).
        _evaluator = (SpecificationEvaluator<TAggregate>)evaluator;
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
    public virtual async Task<IReadOnlyList<TAggregate>> GetByIdsAsync(
        IEnumerable<TId> ids,
        CancellationToken ct = default)
    {
        var idList = ids as ICollection<TId> ?? ids.ToList();
        return await DbContext.Set<TAggregate>()
            .Where(e => idList.Contains(EF.Property<TId>(e, "Id")!))
            .ToListAsync(ct);
    }

    /// <inheritdoc />
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
    public virtual async Task<IReadOnlyList<TResult>> ListProjectedAsync<TResult>(
        IProjectionSpecification<TAggregate, TResult> spec,
        CancellationToken ct = default)
    {
        var projected = _evaluator.GetProjectedQuery(DbContext.Set<TAggregate>(), spec);
        return await projected.ToListAsync(ct);
    }

    /// <inheritdoc />
    public virtual async Task<TResult?> GetBySpecProjectedAsync<TResult>(
        IProjectionSpecification<TAggregate, TResult> spec,
        CancellationToken ct = default)
    {
        var projected = _evaluator.GetProjectedQuery(DbContext.Set<TAggregate>(), spec);
        return await projected.FirstOrDefaultAsync(ct);
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
    }
}

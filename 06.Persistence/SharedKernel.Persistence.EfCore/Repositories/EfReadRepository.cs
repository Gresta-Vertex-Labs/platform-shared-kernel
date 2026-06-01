using Microsoft.EntityFrameworkCore;
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
    /// a composable <see cref="IQueryable{T}"/> pipeline.
    /// </param>
    protected EfReadRepository(
        SharedKernelDbContext dbContext,
        ISpecificationEvaluator<TAggregate> evaluator)
    {
        DbContext = dbContext;
        _evaluator = evaluator;
    }

    /// <inheritdoc />
    public virtual async Task<TAggregate?> GetByIdAsync(TId id, CancellationToken ct = default)
        => await DbContext.Set<TAggregate>().FindAsync([id], ct);

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
}

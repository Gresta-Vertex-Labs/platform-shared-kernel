using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Domain.Specifications;
using SharedKernel.Persistence.Abstractions.Repositories;
using SharedKernel.Persistence.Abstractions.Specifications;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.EfCore.Specifications;

namespace SharedKernel.Persistence.EfCore.Repositories;

/// <summary>
/// Abstract EF Core implementation of the write-side repository contract.
/// Backed by <see cref="DbContext.Set{TEntity}()"/> — never exposes <see cref="IQueryable{T}"/>.
/// </summary>
/// <typeparam name="TAggregate">
/// The aggregate root type. Must implement <see cref="IAggregateRoot{TId}"/>.
/// </typeparam>
/// <typeparam name="TId">The aggregate's identity type. Must be non-null.</typeparam>
/// <remarks>
/// <para>
/// Concrete repositories extend this class and are registered as
/// <c>IRepository&lt;TAggregate, TId&gt;</c> in DI. Do not register this abstract class directly.
/// </para>
/// <para>
/// Mutations staged via <c>AddAsync</c>, <c>UpdateAsync</c>, <c>DeleteAsync</c>, and their range
/// counterparts are not persisted until <c>IUnitOfWork.SaveChangesAsync</c> is called.
/// For soft-deletable aggregates the <c>SoftDeleteInterceptor</c> converts the delete state to
/// a soft-delete before commit.
/// </para>
/// <para>
/// <strong>Update tracking optimisation (P-094):</strong> <see cref="UpdateAsync"/> checks
/// <c>DbContext.Entry(aggregate).State</c> before calling <c>.Update()</c>. Tracked entities
/// (any state other than <see cref="EntityState.Detached"/>) rely on EF change detection — only
/// the actually-modified columns are written. Detached entities get an unconditional <c>.Update()</c>
/// which marks all columns as modified (same as before). The helper
/// <see cref="MarkAsModifiedIfDetached"/> is virtual so subclasses may override the strategy.
/// </para>
/// </remarks>
public abstract class EfRepository<TAggregate, TId> : IRepository<TAggregate, TId>
    where TAggregate : class, IAggregateRoot<TId>
    where TId : notnull
{
    /// <summary>The underlying EF Core context.</summary>
    protected SharedKernelDbContext DbContext { get; }

    private readonly ISpecificationEvaluator<TAggregate> _evaluator;

    /// <summary>
    /// Initialises a new <see cref="EfRepository{TAggregate, TId}"/>.
    /// </summary>
    /// <param name="dbContext">The scoped shared-kernel DB context.</param>
    /// <param name="evaluator">
    /// Optional specification evaluator. Defaults to <see cref="SpecificationEvaluator{T}"/>
    /// when not supplied (e.g., from concrete repositories that only inject the DbContext).
    /// </param>
    protected EfRepository(
        SharedKernelDbContext dbContext,
        ISpecificationEvaluator<TAggregate>? evaluator = null)
    {
        DbContext = dbContext;
        _evaluator = evaluator ?? new SpecificationEvaluator<TAggregate>();
    }

    /// <inheritdoc />
    /// <remarks>
    /// Applies <see cref="ISpecificationEvaluator{T}.GetQuery"/> to the base set, then calls
    /// <c>FirstOrDefaultAsync</c>. The spec's <c>AsNoTracking</c> flag is honored — write-side
    /// callers should leave it unset to receive a tracked entity for subsequent mutations without
    /// requiring an explicit <c>.Update()</c> call. Never returns <see cref="IQueryable{TAggregate}"/>.
    /// </remarks>
    public virtual async Task<TAggregate?> GetBySpecAsync(
        ISpecification<TAggregate> spec,
        CancellationToken ct = default)
    {
        var query = _evaluator.GetQuery(DbContext.Set<TAggregate>(), spec);
        return await query.FirstOrDefaultAsync(ct);
    }

    /// <inheritdoc />
    public virtual async Task<TAggregate?> GetByIdAsync(TId id, CancellationToken ct = default)
        => await DbContext.Set<TAggregate>().FindAsync([id], ct);

    /// <inheritdoc />
    /// <remarks>
    /// Uses an expression-tree predicate (same pattern as <c>ByIdSpecification&lt;TAggregate, TId&gt;</c>)
    /// rather than <c>EF.Property&lt;TId&gt;(e, "Id")</c> — the shadow-property accessor is fragile
    /// on concrete CLR properties. The expression tree is AOT-safe on <see cref="IQueryable{T}"/>.
    /// </remarks>
    public virtual async Task<bool> ExistsAsync(TId id, CancellationToken ct = default)
    {
        var param = Expression.Parameter(typeof(TAggregate), "e");
        var idProperty = Expression.Property(param, "Id");
        var idConstant = Expression.Constant(id, typeof(TId));
        var equals = Expression.Equal(idProperty, idConstant);
        var predicate = Expression.Lambda<Func<TAggregate, bool>>(equals, param);
        return await DbContext.Set<TAggregate>().AnyAsync(predicate, ct);
    }

    /// <inheritdoc />
    public virtual async Task AddAsync(TAggregate aggregate, CancellationToken ct = default)
        => await DbContext.Set<TAggregate>().AddAsync(aggregate, ct);

    /// <inheritdoc />
    /// <remarks>
    /// Delegates to <c>DbContext.Set&lt;TAggregate&gt;().AddRangeAsync</c> which is asynchronous.
    /// Rows are staged in the change tracker and not written to the database until
    /// <c>IUnitOfWork.SaveChangesAsync</c> is called.
    /// </remarks>
    public virtual async Task AddRangeAsync(IEnumerable<TAggregate> aggregates, CancellationToken ct = default)
        => await DbContext.Set<TAggregate>().AddRangeAsync(aggregates, ct);

    /// <inheritdoc />
    public virtual Task UpdateAsync(TAggregate aggregate, CancellationToken ct = default)
    {
        MarkAsModifiedIfDetached(aggregate);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    /// <remarks>
    /// <c>DbContext.UpdateRange</c> is synchronous — this method completes without any async I/O.
    /// The per-entity detached-state check from <see cref="MarkAsModifiedIfDetached"/> is applied
    /// to each aggregate: tracked entities rely on EF change detection (only dirty columns are
    /// written); detached entities get an unconditional <c>.Update()</c> (all columns marked Modified).
    /// Mutations are staged and not persisted until <c>IUnitOfWork.SaveChangesAsync</c> is called.
    /// </remarks>
    public virtual Task UpdateRangeAsync(IEnumerable<TAggregate> aggregates, CancellationToken ct = default)
    {
        // UpdateRange is synchronous in EF Core; apply per-entity detached-state check.
        foreach (var aggregate in aggregates)
            MarkAsModifiedIfDetached(aggregate);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public virtual Task DeleteAsync(TAggregate aggregate, CancellationToken ct = default)
    {
        DbContext.Set<TAggregate>().Remove(aggregate);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Delegates to <c>DbContext.RemoveRange</c> which is synchronous — this method completes
    /// without any async I/O. For <see cref="SharedKernel.Domain.Abstractions.ISoftDeletable"/>
    /// aggregates the <c>SoftDeleteInterceptor</c> converts the <c>Deleted</c> state to
    /// <c>Modified</c> before commit. Mutations are staged and not persisted until
    /// <c>IUnitOfWork.SaveChangesAsync</c> is called.
    /// </remarks>
    public virtual Task DeleteRangeAsync(IEnumerable<TAggregate> aggregates, CancellationToken ct = default)
    {
        DbContext.Set<TAggregate>().RemoveRange(aggregates);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Calls <c>DbContext.Update(aggregate)</c> only when the aggregate is in the
    /// <see cref="EntityState.Detached"/> state. For already-tracked entities EF change detection
    /// handles dirty tracking automatically, avoiding unnecessary full-column UPDATE statements.
    /// </summary>
    /// <param name="aggregate">The aggregate to mark as modified if detached.</param>
    protected virtual void MarkAsModifiedIfDetached(TAggregate aggregate)
    {
        if (DbContext.Entry(aggregate).State == EntityState.Detached)
            DbContext.Set<TAggregate>().Update(aggregate);
    }
}

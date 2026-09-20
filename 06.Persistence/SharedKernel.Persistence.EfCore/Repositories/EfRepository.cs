using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Domain.Specifications;
using SharedKernel.Persistence.Abstractions.Repositories;
using SharedKernel.Persistence.Abstractions.Specifications;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.EfCore.Diagnostics;
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
/// <strong>Update tracking optimisation:</strong> <see cref="UpdateAsync"/> checks
/// <c>DbContext.Entry(aggregate).State</c> before calling <c>.Update()</c>. Tracked entities
/// (any state other than <see cref="EntityState.Detached"/>) rely on EF change detection — only
/// the actually-modified columns are written. Detached entities get an unconditional <c>.Update()</c>
/// which marks all columns as modified (same as before). The helper
/// <see cref="MarkAsModifiedIfDetached"/> is virtual so subclasses may override the strategy.
/// </para>
/// <para>
/// <strong>Observability:</strong> every public write
/// operation on this class — <see cref="GetBySpecAsync"/>, <see cref="GetByIdAsync"/>,
/// <see cref="ExistsAsync"/>, <see cref="AddAsync"/>, <see cref="UpdateAsync"/>,
/// <see cref="DeleteAsync"/>, <see cref="RestoreAsync"/>, and their range counterparts — is
/// wrapped in a distributed-tracing span via
/// <see cref="SharedKernel.Persistence.EfCore.Diagnostics.RepositoryTracing"/>, emitted on
/// <see cref="SharedKernel.Persistence.EfCore.Diagnostics.PersistenceActivitySource"/>
/// (<c>"SharedKernel.Persistence"</c>/<c>"1.0"</c>) and tagged with
/// <see cref="SharedKernel.Persistence.EfCore.Diagnostics.PersistenceTagKeys"/>. Bulk mutations
/// (<see cref="ExecuteUpdateAsync"/>/<see cref="ExecuteDeleteAsync"/>) are the sole exceptions — they
/// are not traced through this helper, consistent with their documented bypass of every other
/// per-entity platform concern (interceptors, domain events).
/// </para>
/// </remarks>
public abstract class EfRepository<TAggregate, TId>
    : IRepository<TAggregate, TId>, IBulkMutationRepository<TAggregate, TId>, IRestorableRepository<TAggregate, TId>
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
    public virtual Task<TAggregate?> GetBySpecAsync(
        ISpecification<TAggregate> spec,
        CancellationToken cancellationToken = default)
        => RepositoryTracing.ExecuteTracedAsync<TAggregate, TAggregate?>(nameof(GetBySpecAsync), async () =>
        {
            KeysetSpecificationGuard.EnsureNotKeyset(spec, nameof(GetBySpecAsync));
            var query = _evaluator.GetQuery(DbContext.Set<TAggregate>(), spec);
            return await query.FirstOrDefaultAsync(cancellationToken);
        });

    /// <inheritdoc />
    /// <remarks>
    /// Wrapped in a distributed-tracing span — previously the only two
    /// <see cref="EfRepository{TAggregate, TId}"/> members not traced via
    /// <see cref="SharedKernel.Persistence.EfCore.Diagnostics.RepositoryTracing"/>.
    /// </remarks>
    public virtual Task<TAggregate?> GetByIdAsync(TId id, CancellationToken cancellationToken = default)
        => RepositoryTracing.ExecuteTracedAsync<TAggregate, TAggregate?>(nameof(GetByIdAsync), async () =>
            await DbContext.Set<TAggregate>().FindAsync([id], cancellationToken));

    /// <inheritdoc />
    /// <remarks>
    /// Uses an expression-tree predicate (same pattern as <c>ByIdSpecification&lt;TAggregate, TId&gt;</c>)
    /// rather than <c>EF.Property&lt;TId&gt;(e, "Id")</c> — the shadow-property accessor is fragile
    /// on concrete CLR properties. The expression tree is AOT-safe on <see cref="IQueryable{T}"/>.
    /// Wrapped in a distributed-tracing span — see <see cref="GetByIdAsync"/>'s remarks.
    /// </remarks>
    public virtual Task<bool> ExistsAsync(TId id, CancellationToken cancellationToken = default)
        => RepositoryTracing.ExecuteTracedAsync<TAggregate, bool>(nameof(ExistsAsync), async () =>
        {
            var holder = new IdHolder(id);
            var param = Expression.Parameter(typeof(TAggregate), "e");
            var idProperty = Expression.Property(param, "Id");
            var idValue = Expression.Field(Expression.Constant(holder), nameof(IdHolder.Id));
            var equals = Expression.Equal(idProperty, idValue);
            var predicate = Expression.Lambda<Func<TAggregate, bool>>(equals, param);
            return await DbContext.Set<TAggregate>().AnyAsync(predicate, cancellationToken);
        });

    // Mimics the shape of a compiler-generated closure display class so a captured id value is
    // recognized and parameterized by EF Core's query-parameter extraction — the
    // same pattern ByIdSpecification<TAggregate,TId> uses — rather than inlined as a SQL literal.
    private sealed class IdHolder(TId id)
    {
        public readonly TId Id = id;
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// Throws <see cref="InvalidOperationException"/> naming
    /// <typeparamref name="TAggregate"/> when it does not implement
    /// <see cref="SharedKernel.Domain.Abstractions.ISoftDeletable"/> — restore has no meaning for a
    /// non-soft-deletable aggregate. Otherwise reuses <see cref="MarkAsModifiedIfDetached"/> so the
    /// entry ends up <see cref="EntityState.Modified"/>, then writes
    /// <c>IsDeleted</c>/<c>DeletedOn</c>/<c>DeletedBy</c> via
    /// <c>ChangeTracker.Entry(entity).CurrentValues[propertyName]</c> — the same mutation rule that
    /// governs <c>AuditInterceptor</c>/<c>SoftDeleteInterceptor</c>.
    /// </para>
    /// <para>
    /// Only STAGES the mutation — a subsequent <c>IUnitOfWork.SaveChangesAsync()</c> persists it.
    /// Restoring an aggregate already not deleted is an idempotent no-op success.
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <typeparamref name="TAggregate"/> does not implement
    /// <see cref="SharedKernel.Domain.Abstractions.ISoftDeletable"/>.
    /// </exception>
    public virtual Task RestoreAsync(TAggregate aggregate, CancellationToken cancellationToken = default)
        => RepositoryTracing.ExecuteTracedAsync<TAggregate>(nameof(RestoreAsync), () =>
        {
            if (aggregate is not ISoftDeletable)
            {
                throw new InvalidOperationException(
                    $"'{typeof(TAggregate).Name}' does not implement " +
                    $"'{nameof(ISoftDeletable)}' — '{nameof(RestoreAsync)}' has no meaning for a " +
                    "non-soft-deletable aggregate.");
            }

            MarkAsModifiedIfDetached(aggregate);

            var entry = DbContext.Entry(aggregate);
            entry.CurrentValues[nameof(ISoftDeletable.IsDeleted)] = false;
            entry.CurrentValues[nameof(ISoftDeletable.DeletedOn)] = null;
            entry.CurrentValues[nameof(ISoftDeletable.DeletedBy)] = null;

            return Task.CompletedTask;
        });

    /// <inheritdoc />
    public virtual Task AddAsync(TAggregate aggregate, CancellationToken cancellationToken = default)
        => RepositoryTracing.ExecuteTracedAsync<TAggregate>(nameof(AddAsync), async () =>
            await DbContext.Set<TAggregate>().AddAsync(aggregate, cancellationToken));

    /// <inheritdoc />
    /// <remarks>
    /// Delegates to <c>DbContext.Set&lt;TAggregate&gt;().AddRangeAsync</c> which is asynchronous.
    /// Rows are staged in the change tracker and not written to the database until
    /// <c>IUnitOfWork.SaveChangesAsync</c> is called.
    /// </remarks>
    public virtual Task AddRangeAsync(IEnumerable<TAggregate> aggregates, CancellationToken cancellationToken = default)
        => RepositoryTracing.ExecuteTracedAsync<TAggregate>(nameof(AddRangeAsync), async () =>
            await DbContext.Set<TAggregate>().AddRangeAsync(aggregates, cancellationToken));

    /// <inheritdoc />
    public virtual Task UpdateAsync(TAggregate aggregate, CancellationToken cancellationToken = default)
        => RepositoryTracing.ExecuteTracedAsync<TAggregate>(nameof(UpdateAsync), () =>
        {
            MarkAsModifiedIfDetached(aggregate);
            return Task.CompletedTask;
        });

    /// <inheritdoc />
    /// <remarks>
    /// <c>DbContext.UpdateRange</c> is synchronous — this method completes without any async I/O.
    /// The per-entity detached-state check from <see cref="MarkAsModifiedIfDetached"/> is applied
    /// to each aggregate: tracked entities rely on EF change detection (only dirty columns are
    /// written); detached entities get an unconditional <c>.Update()</c> (all columns marked Modified).
    /// Mutations are staged and not persisted until <c>IUnitOfWork.SaveChangesAsync</c> is called.
    /// </remarks>
    public virtual Task UpdateRangeAsync(IEnumerable<TAggregate> aggregates, CancellationToken cancellationToken = default)
        => RepositoryTracing.ExecuteTracedAsync<TAggregate>(nameof(UpdateRangeAsync), () =>
        {
            // UpdateRange is synchronous in EF Core; apply per-entity detached-state check.
            foreach (var aggregate in aggregates)
                MarkAsModifiedIfDetached(aggregate);

            return Task.CompletedTask;
        });

    /// <inheritdoc />
    public virtual Task DeleteAsync(TAggregate aggregate, CancellationToken cancellationToken = default)
        => RepositoryTracing.ExecuteTracedAsync<TAggregate>(nameof(DeleteAsync), () =>
        {
            DbContext.Set<TAggregate>().Remove(aggregate);
            return Task.CompletedTask;
        });

    /// <inheritdoc />
    /// <remarks>
    /// Delegates to <c>DbContext.RemoveRange</c> which is synchronous — this method completes
    /// without any async I/O. For <see cref="SharedKernel.Domain.Abstractions.ISoftDeletable"/>
    /// aggregates the <c>SoftDeleteInterceptor</c> converts the <c>Deleted</c> state to
    /// <c>Modified</c> before commit. Mutations are staged and not persisted until
    /// <c>IUnitOfWork.SaveChangesAsync</c> is called.
    /// </remarks>
    public virtual Task DeleteRangeAsync(IEnumerable<TAggregate> aggregates, CancellationToken cancellationToken = default)
        => RepositoryTracing.ExecuteTracedAsync<TAggregate>(nameof(DeleteRangeAsync), () =>
        {
            DbContext.Set<TAggregate>().RemoveRange(aggregates);
            return Task.CompletedTask;
        });

    /// <inheritdoc />
    /// <remarks>
    /// Bypasses the change tracker, <c>IUnitOfWork.SaveChangesAsync</c>, all three platform
    /// interceptors, and domain event dispatch — see <see cref="IBulkMutationRepository{TAggregate,TId}"/>
    /// for details. Only <see cref="ISpecification{TAggregate}.Criteria"/> and
    /// <see cref="ISpecification{TAggregate}.IncludeDeleted"/> are applied; any other specification
    /// shape throws <see cref="UnsupportedSpecificationException"/>.
    /// </remarks>
    public virtual async Task<int> ExecuteUpdateAsync(
        ISpecification<TAggregate> spec,
        Action<UpdateSettersBuilder<TAggregate>> setPropertyCalls,
        CancellationToken cancellationToken = default)
    {
        BulkSpecificationGuard.Validate(spec);
        BulkSpecificationGuard.ValidateSetters(setPropertyCalls);
        var query = BuildBulkQuery(spec);
        return await query.ExecuteUpdateAsync(setPropertyCalls, cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Bypasses the change tracker, <c>IUnitOfWork.SaveChangesAsync</c>, all three platform
    /// interceptors, and domain event dispatch — see <see cref="IBulkMutationRepository{TAggregate,TId}"/>
    /// for details. Always issues a hard physical <c>DELETE</c>, even for
    /// <see cref="SharedKernel.Domain.Abstractions.ISoftDeletable"/> aggregates. Only
    /// <see cref="ISpecification{TAggregate}.Criteria"/> and
    /// <see cref="ISpecification{TAggregate}.IncludeDeleted"/> are applied; any other specification
    /// shape throws <see cref="UnsupportedSpecificationException"/>.
    /// </remarks>
    public virtual async Task<int> ExecuteDeleteAsync(
        ISpecification<TAggregate> spec,
        CancellationToken cancellationToken = default)
    {
        BulkSpecificationGuard.Validate(spec);
        var query = BuildBulkQuery(spec);
        return await query.ExecuteDeleteAsync(cancellationToken);
    }

    // Builds an IQueryable<TAggregate> applying ONLY IgnoreQueryFilters (when IncludeDeleted) and
    // Criteria — the only specification shapes meaningful for a single ExecuteUpdate/ExecuteDelete
    // statement. Does not use ISpecificationEvaluator<T>.GetQuery, which applies the full pipeline.
    //
    // IgnoreQueryFilters is SELECTIVE — only the named "SoftDelete" filter is
    // dropped. The tenant filter (named "Tenant") is NEVER dropped here, so a bulk mutation issued
    // through a tenanted repository stays scoped to the current tenant even when IncludeDeleted is
    // set; bypassing tenant isolation requires the explicit ICrossTenantScope escape hatch, not this
    // flag.
    private IQueryable<TAggregate> BuildBulkQuery(ISpecification<TAggregate> spec)
    {
        var query = DbContext.Set<TAggregate>().AsQueryable();

        if (spec.IncludeDeleted)
            query = query.IgnoreQueryFilters([PersistenceFilterNames.SoftDelete]);

        if (spec.Criteria is not null)
            query = query.Where(spec.Criteria);

        return query;
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

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Query;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Domain.Specifications;
using SharedKernel.Persistence.Abstractions.Repositories;
using SharedKernel.Persistence.EfCore.Concurrency;
using SharedKernel.Persistence.EfCore.Context;
using SharedKernel.Persistence.EfCore.Diagnostics;
using SharedKernel.Persistence.EfCore.Specifications;

namespace SharedKernel.Persistence.EfCore.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IRepository{TAggregate, TId}"/> and
/// <see cref="IBulkMutationRepository{TAggregate, TId}"/>.
/// </summary>
/// <typeparam name="TAggregate">The aggregate root type.</typeparam>
/// <typeparam name="TId">The aggregate's identity type.</typeparam>
/// <remarks>
/// <para>
/// <b>No class needed.</b> Both interfaces are registered automatically for every aggregate the service's
/// DbContexts map. Derive from this class only for custom queries or to override
/// <see cref="EfReadRepository{TAggregate, TId}.AggregateQuery"/>.
/// </para>
/// <para>
/// <b>Tracking.</b> <see cref="GetByIdAsync"/>, <see cref="FirstOrDefaultAsync"/> and <see cref="ListAsync"/>
/// return tracked aggregates (even when the context's default is no-tracking). Through an
/// <see cref="IReadRepository{TAggregate, TId}"/> reference the same object answers untracked, as that contract
/// promises; every other read member is always untracked.
/// </para>
/// </remarks>
#pragma warning disable RS0026 // Mirrors IRepository: the expected-version UpdateAsync overload keeps the optional token last.
public class EfRepository<TAggregate, TId>
    : EfReadRepository<TAggregate, TId>, IRepository<TAggregate, TId>, IBulkMutationRepository<TAggregate, TId>
    where TAggregate : class, IAggregateRoot<TId>
    where TId : notnull
{
    /// <summary>Initializes a new repository over <paramref name="dbContext"/>.</summary>
    /// <param name="dbContext">The context that maps <typeparamref name="TAggregate"/>.</param>
    public EfRepository(SharedKernelDbContext dbContext)
        : base(dbContext)
    {
    }

    /// <summary>Test seam: a repository over an explicit specification evaluator.</summary>
    internal EfRepository(SharedKernelDbContext dbContext, ISpecificationEvaluator<TAggregate>? evaluator)
        : base(dbContext, evaluator)
    {
    }

    /// <summary>Returns the tracked aggregate with the given identity, or <see langword="null"/>.</summary>
    /// <param name="id">The aggregate's identity.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The complete aggregate, tracked; or <see langword="null"/>.</returns>
    public new virtual Task<TAggregate?> GetByIdAsync(TId id, CancellationToken cancellationToken = default) =>
        RepositoryTracing.ExecuteTracedAsync<TAggregate, TAggregate?>(nameof(GetByIdAsync), () =>
            AggregateQuery().AsTracking()
                .Where(RepositoryExpressions<TAggregate, TId>.ById(id))
                .FirstOrDefaultAsync(cancellationToken));

    /// <summary>Returns the first tracked aggregate matching <paramref name="spec"/>, or <see langword="null"/>.</summary>
    /// <param name="spec">The query.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The first match, tracked; or <see langword="null"/>.</returns>
    public new virtual Task<TAggregate?> FirstOrDefaultAsync(ISpecification<TAggregate> spec, CancellationToken cancellationToken = default) =>
        RepositoryTracing.ExecuteTracedAsync<TAggregate, TAggregate?>(nameof(FirstOrDefaultAsync), () =>
            Query(spec).AsTracking().FirstOrDefaultAsync(cancellationToken));

    /// <summary>Returns every aggregate matching <paramref name="spec"/>, tracked.</summary>
    /// <param name="spec">The query.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The matches, tracked.</returns>
    public new virtual Task<IReadOnlyList<TAggregate>> ListAsync(ISpecification<TAggregate> spec, CancellationToken cancellationToken = default) =>
        RepositoryTracing.ExecuteTracedAsync<TAggregate, IReadOnlyList<TAggregate>>(nameof(ListAsync), async () =>
            await Query(spec).AsTracking().ToListAsync(cancellationToken).ConfigureAwait(false));

    // Through the read contract the same object stays untracked.
    Task<TAggregate?> IReadRepository<TAggregate, TId>.GetByIdAsync(TId id, CancellationToken cancellationToken) =>
        base.GetByIdAsync(id, cancellationToken);

    Task<TAggregate?> IReadRepository<TAggregate, TId>.FirstOrDefaultAsync(ISpecification<TAggregate> spec, CancellationToken cancellationToken) =>
        base.FirstOrDefaultAsync(spec, cancellationToken);

    Task<IReadOnlyList<TAggregate>> IReadRepository<TAggregate, TId>.ListAsync(ISpecification<TAggregate> spec, CancellationToken cancellationToken) =>
        base.ListAsync(spec, cancellationToken);

    /// <inheritdoc />
    public virtual Task AddAsync(TAggregate aggregate, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        return Traced(nameof(AddAsync), () => DbContext.Set<TAggregate>().Add(aggregate));
    }

    /// <inheritdoc />
    public virtual Task AddRangeAsync(IEnumerable<TAggregate> aggregates, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(aggregates);
        return Traced(nameof(AddRangeAsync), () => DbContext.Set<TAggregate>().AddRange(aggregates));
    }

    /// <inheritdoc />
    public virtual Task UpdateAsync(TAggregate aggregate, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        return Traced(nameof(UpdateAsync), () =>
        {
            ThrowIfDetachedWithoutVersion(aggregate, "UpdateAsync(aggregate, expectedVersion)");
            AttachIfDetached(aggregate);
        });
    }

    /// <inheritdoc />
    public virtual Task UpdateAsync(TAggregate aggregate, EntityVersion expectedVersion, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(aggregate);

        return Traced(nameof(UpdateAsync), () =>
        {
            // Opened before anything is attached: a version that is not one of this aggregate (another aggregate's,
            // altered, or sealed with a key this service does not know) is a conflict and leaves the tracker untouched.
            var expected = ConcurrencyVersion.ResolveExpected(DbContext, aggregate, expectedVersion);
            AttachIfDetached(aggregate);

            // The UPDATE's WHERE clause compares the version token's ORIGINAL value; an unchanged entity already loaded
            // at another version fails immediately. Either way the conflict carries the current version.
            ConcurrencyVersion.ApplyExpected(DbContext, aggregate, expected);
        });
    }

    /// <inheritdoc />
    public virtual Task UpdateRangeAsync(IEnumerable<TAggregate> aggregates, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(aggregates);
        return Traced(nameof(UpdateRangeAsync), () =>
        {
            var list = aggregates as IReadOnlyList<TAggregate> ?? aggregates.ToList();
            foreach (var aggregate in list)
                ThrowIfDetachedWithoutVersion(aggregate, "UpdateAsync(aggregate, expectedVersion) for each aggregate");

            foreach (var aggregate in list)
                AttachIfDetached(aggregate);
        });
    }

    /// <inheritdoc />
    public virtual Task DeleteAsync(TAggregate aggregate, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        return Traced(nameof(DeleteAsync), () =>
        {
            ThrowIfDetachedWithoutVersion(aggregate, "DeleteAsync(aggregate, expectedVersion)");
            DbContext.Set<TAggregate>().Remove(aggregate);
        });
    }

    /// <inheritdoc />
    public virtual Task DeleteAsync(TAggregate aggregate, EntityVersion expectedVersion, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        return Traced(nameof(DeleteAsync), () =>
        {
            var expected = ConcurrencyVersion.ResolveExpected(DbContext, aggregate, expectedVersion);
            DbContext.Set<TAggregate>().Remove(aggregate);
            ConcurrencyVersion.ApplyExpected(DbContext, aggregate, expected);
        });
    }

    /// <inheritdoc />
    public virtual Task DeleteRangeAsync(IEnumerable<TAggregate> aggregates, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(aggregates);
        return Traced(nameof(DeleteRangeAsync), () =>
        {
            var list = aggregates as IReadOnlyList<TAggregate> ?? aggregates.ToList();
            foreach (var aggregate in list)
                ThrowIfDetachedWithoutVersion(aggregate, "DeleteAsync(aggregate, expectedVersion) for each aggregate");

            DbContext.Set<TAggregate>().RemoveRange(list);
        });
    }


    /// <inheritdoc />
    public virtual Task<int> ExecuteUpdateAsync(
        ISpecification<TAggregate> spec,
        Action<BulkUpdateSetters<TAggregate>> setters,
        CancellationToken cancellationToken = default)
    {
        BulkSpecificationGuard.Validate(spec);
        ArgumentNullException.ThrowIfNull(setters);

        var recorded = new BulkUpdateSetters<TAggregate>();
        setters(recorded);
        var targets = BulkSpecificationGuard.ValidateSetters(recorded, EntityType);

        var stampModified = typeof(IHasAudit).IsAssignableFrom(typeof(TAggregate))
            && !targets.Any(p => p.DeclaringType is IEntityType
                && p.Name is nameof(IHasAudit.ModifiedOn) or nameof(IHasAudit.ModifiedBy));

        var extra = stampModified ? ModifiedSetters(DbContext.Clock.UtcNow, DbContext.CurrentActorId) : null;

        return RepositoryTracing.ExecuteTracedAsync<TAggregate, int>(nameof(ExecuteUpdateAsync), () =>
            BulkQuery(spec).ExecuteUpdateAsync(EfBulkUpdateSetters.ToEfSetters(recorded, extra), cancellationToken));
    }

    /// <inheritdoc />
    public virtual Task<int> ExecuteDeleteAsync(ISpecification<TAggregate> spec, CancellationToken cancellationToken = default)
    {
        BulkSpecificationGuard.Validate(spec);

        if (!typeof(ISoftDeletable).IsAssignableFrom(typeof(TAggregate)))
        {
            return RepositoryTracing.ExecuteTracedAsync<TAggregate, int>(nameof(ExecuteDeleteAsync), () =>
                BulkQuery(spec).ExecuteDeleteAsync(cancellationToken));
        }

        // A soft-deletable aggregate is soft-deleted in bulk too (A5). Rows already deleted keep their
        // original deletion time and actor.
        var now = DbContext.Clock.UtcNow;
        var actor = DbContext.CurrentActorId;
        var stampModified = typeof(IHasAudit).IsAssignableFrom(typeof(TAggregate));

        var query = BulkQuery(spec).Where(RepositoryExpressions<TAggregate, TId>.NotDeleted());

        return RepositoryTracing.ExecuteTracedAsync<TAggregate, int>(nameof(ExecuteDeleteAsync), () =>
            query.ExecuteUpdateAsync(
                builder =>
                {
                    builder.SetProperty(RepositoryExpressions<TAggregate, TId>.Property<bool>(nameof(ISoftDeletable.IsDeleted)), true);
                    builder.SetProperty(RepositoryExpressions<TAggregate, TId>.Property<DateTimeOffset?>(nameof(ISoftDeletable.DeletedOn)), now);
                    builder.SetProperty(RepositoryExpressions<TAggregate, TId>.Property<string?>(nameof(ISoftDeletable.DeletedBy)), actor);

                    if (stampModified)
                        ModifiedSetters(now, actor)(builder);
                },
                cancellationToken));
    }

    /// <inheritdoc />
    public virtual Task<int> ExecutePurgeAsync(ISpecification<TAggregate> spec, CancellationToken cancellationToken = default)
    {
        BulkSpecificationGuard.Validate(spec);

        return RepositoryTracing.ExecuteTracedAsync<TAggregate, int>(nameof(ExecutePurgeAsync), () =>
            BulkQuery(spec).ExecuteDeleteAsync(cancellationToken));
    }

    /// <summary>
    /// Attaches a detached aggregate as modified (every column written); a tracked aggregate is left to change
    /// detection, so only changed columns are written.
    /// </summary>
    /// <param name="aggregate">The aggregate.</param>
    protected virtual void AttachIfDetached(TAggregate aggregate)
    {
        if (DbContext.Entry(aggregate).State == EntityState.Detached)
            DbContext.Set<TAggregate>().Update(aggregate);
    }

    // A detached aggregate whose version only the database knows (the shadow xmin) carries no original version: attaching
    // it would compare against 0 and always conflict. The caller must supply the version it based the change on.
    private void ThrowIfDetachedWithoutVersion(TAggregate aggregate, string alternative)
    {
        if (DbContext.Entry(aggregate).State != EntityState.Detached || !ConcurrencyVersion.IsKeptByDatabase(EntityType))
            return;

        throw new InvalidOperationException(
            $"'{typeof(TAggregate).Name}' is not tracked by this context and its version is kept by the database (xmin), "
            + "so the version this change is based on is unknown. Call " + alternative + " with the version the client "
            + "last read (its ETag / If-Match), or load the aggregate in this scope and change the tracked instance.");
    }

    private IEntityType EntityType =>
        DbContext.Model.FindEntityType(typeof(TAggregate))
        ?? throw new InvalidOperationException(
            $"'{typeof(TAggregate).Name}' is not mapped by '{DbContext.GetType().Name}'.");

    // Only criteria and the selective soft-delete bypass; the tenant filter always stays.
    private IQueryable<TAggregate> BulkQuery(ISpecification<TAggregate> spec)
    {
        IQueryable<TAggregate> query = DbContext.Set<TAggregate>().TagWith(spec.GetType().Name);

        if (spec.IncludeDeleted)
            query = query.IgnoreQueryFilters([PersistenceFilterNames.SoftDelete]);

        if (spec.Criteria is not null)
            query = query.Where(spec.Criteria);

        return query;
    }

    private static Action<UpdateSettersBuilder<TAggregate>> ModifiedSetters(DateTimeOffset now, string actor) =>
        builder =>
        {
            builder.SetProperty(RepositoryExpressions<TAggregate, TId>.Property<DateTimeOffset?>(nameof(IHasAudit.ModifiedOn)), now);
            builder.SetProperty(RepositoryExpressions<TAggregate, TId>.Property<string?>(nameof(IHasAudit.ModifiedBy)), actor);
        };

    // Staging is in-memory work, but it keeps a span per operation like every other repository call.
    private static Task Traced(string operation, Action stage) =>
        RepositoryTracing.ExecuteTracedAsync<TAggregate>(operation, () =>
        {
            stage();
            return Task.CompletedTask;
        });
}
#pragma warning restore RS0026

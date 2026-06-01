using Microsoft.EntityFrameworkCore;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Persistence.Abstractions.Repositories;
using SharedKernel.Persistence.EfCore.Context;

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
/// Mutations staged via <c>AddAsync</c>, <c>UpdateAsync</c>, and <c>DeleteAsync</c> are not
/// persisted until <c>IUnitOfWork.SaveChangesAsync</c> is called. For soft-deletable aggregates
/// the <c>SoftDeleteInterceptor</c> converts the delete state to a soft-delete before commit.
/// </para>
/// </remarks>
public abstract class EfRepository<TAggregate, TId> : IRepository<TAggregate, TId>
    where TAggregate : class, IAggregateRoot<TId>
    where TId : notnull
{
    /// <summary>The underlying EF Core context.</summary>
    protected SharedKernelDbContext DbContext { get; }

    /// <summary>
    /// Initialises a new <see cref="EfRepository{TAggregate, TId}"/>.
    /// </summary>
    /// <param name="dbContext">The scoped shared-kernel DB context.</param>
    protected EfRepository(SharedKernelDbContext dbContext)
    {
        DbContext = dbContext;
    }

    /// <inheritdoc />
    public virtual async Task<TAggregate?> GetByIdAsync(TId id, CancellationToken ct = default)
        => await DbContext.Set<TAggregate>().FindAsync([id], ct);

    /// <inheritdoc />
    public virtual async Task AddAsync(TAggregate aggregate, CancellationToken ct = default)
        => await DbContext.Set<TAggregate>().AddAsync(aggregate, ct);

    /// <inheritdoc />
    public virtual Task UpdateAsync(TAggregate aggregate, CancellationToken ct = default)
    {
        DbContext.Set<TAggregate>().Update(aggregate);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public virtual Task DeleteAsync(TAggregate aggregate, CancellationToken ct = default)
    {
        DbContext.Set<TAggregate>().Remove(aggregate);
        return Task.CompletedTask;
    }
}

using SharedKernel.Domain.Abstractions;

namespace SharedKernel.Persistence.Abstractions.Repositories;

/// <summary>
/// Write-side repository contract for a DDD aggregate root.
/// Provides the minimal command surface: fetch by identity, add, update, and delete.
/// </summary>
/// <typeparam name="TAggregate">
/// The aggregate root type. Must implement <see cref="IAggregateRoot{TId}"/>.
/// </typeparam>
/// <typeparam name="TId">
/// The aggregate's identity type. Must be non-null.
/// </typeparam>
/// <remarks>
/// <para>
/// This interface is intentionally write-only — it does not expose <see cref="System.Linq.IQueryable{T}"/>,
/// raw SQL, or any query surface. All reads are handled by <see cref="IReadRepository{TAggregate,TId}"/>.
/// </para>
/// <para>
/// Mutations staged via <c>AddAsync</c>, <c>UpdateAsync</c>, and <c>DeleteAsync</c> are not persisted
/// until <see cref="IUnitOfWork.SaveChangesAsync"/> is called. Never call <c>DbContext.SaveChangesAsync</c>
/// directly — that is a hard violation of the save-boundary rule.
/// </para>
/// </remarks>
public interface IRepository<TAggregate, TId>
    where TAggregate : IAggregateRoot<TId>
    where TId : notnull
{
    /// <summary>
    /// Retrieves an aggregate by its unique identity, or <see langword="null"/> when not found.
    /// </summary>
    /// <param name="id">The aggregate's unique identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The aggregate root, or <see langword="null"/> if no match exists.</returns>
    Task<TAggregate?> GetByIdAsync(TId id, CancellationToken ct = default);

    /// <summary>
    /// Stages a new aggregate for insertion during the next <see cref="IUnitOfWork.SaveChangesAsync"/> call.
    /// </summary>
    /// <param name="aggregate">The aggregate to insert.</param>
    /// <param name="ct">Cancellation token.</param>
    Task AddAsync(TAggregate aggregate, CancellationToken ct = default);

    /// <summary>
    /// Stages an existing aggregate for update during the next <see cref="IUnitOfWork.SaveChangesAsync"/> call.
    /// </summary>
    /// <param name="aggregate">The aggregate to update.</param>
    /// <param name="ct">Cancellation token.</param>
    Task UpdateAsync(TAggregate aggregate, CancellationToken ct = default);

    /// <summary>
    /// Stages an aggregate for deletion during the next <see cref="IUnitOfWork.SaveChangesAsync"/> call.
    /// For <see cref="SharedKernel.Domain.Abstractions.ISoftDeletable"/> aggregates, the
    /// persistence layer converts this to a soft-delete mutation rather than a physical row removal.
    /// </summary>
    /// <param name="aggregate">The aggregate to delete.</param>
    /// <param name="ct">Cancellation token.</param>
    Task DeleteAsync(TAggregate aggregate, CancellationToken ct = default);
}

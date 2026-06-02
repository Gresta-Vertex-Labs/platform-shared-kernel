using SharedKernel.Domain.Abstractions;

namespace SharedKernel.Persistence.Abstractions.Repositories;

/// <summary>
/// Write-side repository contract for a DDD aggregate root.
/// Provides the minimal command surface: fetch by identity, add, update, delete, and bulk variants.
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
/// Mutations staged via <c>AddAsync</c>, <c>UpdateAsync</c>, <c>DeleteAsync</c>, and their range
/// counterparts are not persisted until <see cref="IUnitOfWork.SaveChangesAsync"/> is called.
/// Never call <c>DbContext.SaveChangesAsync</c> directly — that is a hard violation of the
/// save-boundary rule.
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
    /// Returns <see langword="true"/> when an aggregate with the given identity exists in the store.
    /// </summary>
    /// <param name="id">The aggregate's unique identifier to check.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// <see langword="true"/> if a record exists; otherwise <see langword="false"/>.
    /// </returns>
    /// <remarks>
    /// Issues an <c>EXISTS</c>/<c>ANY</c> check — never materialises the aggregate.
    /// O(1) at the database.
    /// </remarks>
    Task<bool> ExistsAsync(TId id, CancellationToken ct = default);

    /// <summary>
    /// Stages a new aggregate for insertion during the next <see cref="IUnitOfWork.SaveChangesAsync"/> call.
    /// </summary>
    /// <param name="aggregate">The aggregate to insert.</param>
    /// <param name="ct">Cancellation token.</param>
    Task AddAsync(TAggregate aggregate, CancellationToken ct = default);

    /// <summary>
    /// Stages multiple aggregates for insertion during the next <see cref="IUnitOfWork.SaveChangesAsync"/> call.
    /// </summary>
    /// <param name="aggregates">The aggregates to insert. Must not be <see langword="null"/>.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="ct"/> is cancelled.</exception>
    Task AddRangeAsync(IEnumerable<TAggregate> aggregates, CancellationToken ct = default);

    /// <summary>
    /// Stages an existing aggregate for update during the next <see cref="IUnitOfWork.SaveChangesAsync"/> call.
    /// </summary>
    /// <param name="aggregate">The aggregate to update.</param>
    /// <param name="ct">Cancellation token.</param>
    Task UpdateAsync(TAggregate aggregate, CancellationToken ct = default);

    /// <summary>
    /// Stages multiple existing aggregates for update during the next <see cref="IUnitOfWork.SaveChangesAsync"/> call.
    /// </summary>
    /// <param name="aggregates">The aggregates to update. Must not be <see langword="null"/>.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <remarks>
    /// Staging semantics are identical to <see cref="UpdateAsync"/> — mutations are not persisted
    /// until <see cref="IUnitOfWork.SaveChangesAsync"/> is called.
    /// </remarks>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="ct"/> is cancelled.</exception>
    Task UpdateRangeAsync(IEnumerable<TAggregate> aggregates, CancellationToken ct = default);

    /// <summary>
    /// Stages an aggregate for deletion during the next <see cref="IUnitOfWork.SaveChangesAsync"/> call.
    /// For <see cref="SharedKernel.Domain.Abstractions.ISoftDeletable"/> aggregates, the
    /// persistence layer converts this to a soft-delete mutation rather than a physical row removal.
    /// </summary>
    /// <param name="aggregate">The aggregate to delete.</param>
    /// <param name="ct">Cancellation token.</param>
    Task DeleteAsync(TAggregate aggregate, CancellationToken ct = default);

    /// <summary>
    /// Stages multiple aggregates for deletion during the next <see cref="IUnitOfWork.SaveChangesAsync"/> call.
    /// For <see cref="SharedKernel.Domain.Abstractions.ISoftDeletable"/> aggregates, the
    /// persistence layer converts each delete to a soft-delete mutation.
    /// </summary>
    /// <param name="aggregates">The aggregates to delete. Must not be <see langword="null"/>.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="ct"/> is cancelled.</exception>
    Task DeleteRangeAsync(IEnumerable<TAggregate> aggregates, CancellationToken ct = default);
}

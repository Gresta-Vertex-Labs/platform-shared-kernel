namespace SharedKernel.Persistence.Abstractions.UnitOfWork;

/// <summary>
/// Provider-agnostic transaction handle returned by
/// <see cref="ITransactionalUnitOfWork.BeginTransactionAsync"/>.
/// </summary>
/// <remarks>
/// <para>
/// Callers must <c>await using</c> this handle (or explicitly call
/// <see cref="IAsyncDisposable.DisposeAsync()"/>) to release the underlying database
/// transaction resource, regardless of whether the transaction was committed or rolled back.
/// </para>
/// <para>
/// The EF Core implementation wraps <c>IDbContextTransaction</c> — EF Core disposes the
/// underlying connection resources when <see cref="IAsyncDisposable.DisposeAsync()"/> is called.
/// </para>
/// <para>
/// Zero ORM dependencies — this interface lives in <c>SharedKernel.Persistence.Abstractions</c>
/// using BCL types only.
/// </para>
/// </remarks>
public interface IPersistenceTransaction : IAsyncDisposable
{
    /// <summary>
    /// Commits all operations performed within the transaction to the backing store.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <see cref="Task"/> that completes when the commit succeeds.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the transaction has already been committed or rolled back.
    /// </exception>
    Task CommitAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Rolls back all operations performed within the transaction.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <see cref="Task"/> that completes when the rollback succeeds.</returns>
    /// <remarks>
    /// Safe to call even if no operations have been staged. A rollback does not throw when the
    /// transaction is in a valid state and has not already been committed.
    /// </remarks>
    Task RollbackAsync(CancellationToken cancellationToken = default);
}

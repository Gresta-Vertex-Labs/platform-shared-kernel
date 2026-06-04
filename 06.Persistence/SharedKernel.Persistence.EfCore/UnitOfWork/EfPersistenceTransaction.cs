using Microsoft.EntityFrameworkCore.Storage;
using SharedKernel.Persistence.Abstractions.UnitOfWork;

namespace SharedKernel.Persistence.EfCore.UnitOfWork;

/// <summary>
/// EF Core implementation of <see cref="IPersistenceTransaction"/> that wraps an
/// <see cref="IDbContextTransaction"/>.
/// </summary>
/// <remarks>
/// <para>
/// Callers must <c>await using</c> this handle to dispose the underlying
/// <see cref="IDbContextTransaction"/> and release database resources.
/// </para>
/// <para>
/// Domain event dispatch fires after <see cref="CommitAsync"/> in
/// <see cref="EfTransactionalUnitOfWork"/>, consistent with <c>EfUnitOfWork</c> semantics.
/// </para>
/// </remarks>
internal sealed class EfPersistenceTransaction : IPersistenceTransaction
{
    private readonly IDbContextTransaction _transaction;

    internal EfPersistenceTransaction(IDbContextTransaction transaction)
    {
        _transaction = transaction;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Delegates to the underlying <see cref="IDbContextTransaction.CommitAsync"/>.
    /// Domain event dispatch is NOT performed here — use
    /// <see cref="EfTransactionalUnitOfWork"/> (via <c>ITransactionalUnitOfWork</c>) to obtain a
    /// transaction handle that dispatches events after commit.
    /// </remarks>
    public Task CommitAsync(CancellationToken ct = default)
        => _transaction.CommitAsync(ct);

    /// <inheritdoc />
    /// <remarks>
    /// Domain events are <strong>not</strong> dispatched on rollback. The change-tracker still
    /// holds staged events; callers must discard the unit-of-work scope after a rollback.
    /// </remarks>
    public Task RollbackAsync(CancellationToken ct = default)
        => _transaction.RollbackAsync(ct);

    /// <inheritdoc />
    public ValueTask DisposeAsync()
        => _transaction.DisposeAsync();
}

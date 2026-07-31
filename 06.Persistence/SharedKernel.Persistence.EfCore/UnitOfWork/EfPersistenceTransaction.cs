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
/// Domain event dispatch fires after <see cref="CommitAsync"/> completes successfully, consistent
/// with <c>EfUnitOfWork.SaveChangesAsync</c> semantics — dispatch is delegated back to the owning
/// <see cref="EfTransactionalUnitOfWork"/>'s <c>DispatchAndClearEventsAsync</c>.
/// </para>
/// </remarks>
internal sealed class EfPersistenceTransaction : IPersistenceTransaction
{
    private readonly IDbContextTransaction _transaction;
    private readonly EfTransactionalUnitOfWork _owner;

    internal EfPersistenceTransaction(IDbContextTransaction transaction, EfTransactionalUnitOfWork owner)
    {
        _transaction = transaction;
        _owner = owner;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Commits the underlying <see cref="IDbContextTransaction"/>, then dispatches and clears any
    /// domain events collected during the transaction — consistent with
    /// <c>EfUnitOfWork.SaveChangesAsync</c>'s post-commit dispatch semantics.
    /// </remarks>
    public async Task CommitAsync(CancellationToken ct = default)
    {
        await _transaction.CommitAsync(ct);
        await _owner.DispatchAndClearEventsAsync(ct);
    }

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

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
/// <strong>Domain events:</strong> no longer dispatches domain events itself — dispatch now
/// happens inside every <c>EfTransactionalUnitOfWork.SaveChangesAsync</c> call, BEFORE the physical
/// save, so it is already complete by the time <see cref="CommitAsync"/> runs. A caller MUST call
/// <c>IUnitOfWork.SaveChangesAsync</c> at least once within this transaction's scope before
/// <see cref="CommitAsync"/> for anything — including domain events — to take effect; committing an
/// empty transaction with no prior save is a legitimate no-op.
/// </para>
/// </remarks>
internal sealed class EfPersistenceTransaction : IPersistenceTransaction
{
    private readonly IDbContextTransaction _transaction;
    private readonly AmbientDbTransactionAccessor? _ambientTransactionAccessor;

    /// <summary>
    /// Initialises a new <see cref="EfPersistenceTransaction"/>, publishing
    /// <paramref name="connection"/>/<paramref name="transaction"/> through
    /// <paramref name="ambientTransactionAccessor"/> so a Dapper command service resolving
    /// <c>SharedKernel.Persistence.Abstractions.Coordination.IAmbientDbTransaction</c> can enlist in
    /// this same transaction for the remainder of its scope.
    /// </summary>
    internal EfPersistenceTransaction(
        IDbContextTransaction transaction,
        System.Data.Common.DbConnection connection,
        AmbientDbTransactionAccessor? ambientTransactionAccessor = null)
    {
        _transaction = transaction;
        _ambientTransactionAccessor = ambientTransactionAccessor;

        if (_ambientTransactionAccessor is not null)
            _ambientTransactionAccessor.Current = (connection, transaction.GetDbTransaction());
    }

    /// <inheritdoc />
    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _transaction.CommitAsync(cancellationToken);
        }
        finally
        {
            ClearAmbient();
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// <strong>Pre-commit dispatch caveat:</strong> if <c>IUnitOfWork.SaveChangesAsync</c> was called
    /// within this transaction's scope before rolling back, any domain events it raised were ALREADY
    /// dispatched (pre-commit, in-process) even though the underlying data change is now rolled
    /// back. This is why <c>IDomainEventDispatcher</c> handlers must only affect the SAME
    /// <see cref="Microsoft.EntityFrameworkCore.DbContext"/> (so their effects roll back atomically
    /// too) — see <c>DomainEventDispatchLoop</c>'s remarks. A handler with a genuinely external
    /// effect must never be registered here.
    /// </remarks>
    public async Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _transaction.RollbackAsync(cancellationToken);
        }
        finally
        {
            ClearAmbient();
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        try
        {
            await _transaction.DisposeAsync();
        }
        finally
        {
            ClearAmbient();
        }
    }

    // Clearing is idempotent — Commit/Rollback/Dispose can each observe the ambient slot already
    // cleared by whichever of the three ran first.
    private void ClearAmbient()
    {
        if (_ambientTransactionAccessor is not null)
            _ambientTransactionAccessor.Current = null;
    }
}

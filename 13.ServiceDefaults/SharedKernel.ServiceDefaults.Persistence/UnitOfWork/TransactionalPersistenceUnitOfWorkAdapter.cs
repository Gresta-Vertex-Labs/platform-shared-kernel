using AppIPersistenceTransaction = SharedKernel.Application.Behaviors.Transaction.IPersistenceTransaction;
using AppITransactionalUnitOfWork = SharedKernel.Application.Behaviors.Transaction.ITransactionalUnitOfWork;
using PersistenceIPersistenceTransaction = SharedKernel.Persistence.Abstractions.UnitOfWork.IPersistenceTransaction;
using PersistenceITransactionalUnitOfWork = SharedKernel.Persistence.Abstractions.UnitOfWork.ITransactionalUnitOfWork;

namespace SharedKernel.ServiceDefaults.Persistence.UnitOfWork;

/// <summary>
/// Bridges <c>06.Persistence</c>'s <see cref="PersistenceITransactionalUnitOfWork"/> to
/// <c>05.Application.Behaviors</c>'s smaller <see cref="AppITransactionalUnitOfWork"/> local seam, so
/// <c>TransactionBehavior</c> can open an explicit transaction BEFORE the inner pipeline runs and
/// commit or roll it back once that pipeline's outcome is known.
/// </summary>
/// <remarks>
/// <para>
/// Registered in place of the plain <see cref="PersistenceUnitOfWorkAdapter"/> only when
/// <c>06.Persistence</c>'s real <see cref="PersistenceITransactionalUnitOfWork"/> is itself resolvable
/// in the same DI scope — i.e. the consuming service called
/// <c>EfCorePersistenceBuilder{TContext}.WithTransactionalUnitOfWork()</c>. See
/// <see cref="Extensions.PersistenceSecurityExtensions.WithApplicationTransactionBehavior{TContext}"/>
/// for exactly how that choice is made.
/// </para>
/// <para>
/// This closes a previously-shipped gap: <c>SharedKernel.Persistence.EfCore.Auditing</c>'s
/// <c>EfAuditTrailWriter</c> requires an ambient <c>IAmbientDbTransaction.Current</c> to record a
/// successful-outcome audit entry, but nothing in <c>05.Application.Behaviors</c> or
/// <c>13.ServiceDefaults</c> ever opened one — <c>TransactionBehavior</c> only ever called
/// <c>SaveChangesAsync</c>, never <c>BeginTransactionAsync</c>/<c>ExecuteInTransactionAsync</c>. This
/// adapter is what lets <c>TransactionBehavior</c> open that transaction itself, so the audit write
/// (staged by <c>AuditingBehavior</c>, registered inner to <c>TransactionBehavior</c> in the canonical
/// command stage) and the business write it attests to commit — or roll back — atomically together.
/// </para>
/// </remarks>
public sealed class TransactionalPersistenceUnitOfWorkAdapter : AppITransactionalUnitOfWork
{
    private readonly PersistenceITransactionalUnitOfWork _inner;

    /// <summary>Initialises a new <see cref="TransactionalPersistenceUnitOfWorkAdapter"/>.</summary>
    /// <param name="inner">The scoped <c>06.Persistence</c> transactional unit of work to delegate to.</param>
    public TransactionalPersistenceUnitOfWorkAdapter(PersistenceITransactionalUnitOfWork inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        _inner = inner;
    }

    /// <inheritdoc />
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken) =>
        _inner.SaveChangesAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<AppIPersistenceTransaction> BeginTransactionAsync(CancellationToken cancellationToken)
    {
        var transaction = await _inner.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        return new PersistenceTransactionAdapter(transaction);
    }

    // Adapts 06.Persistence's richer IPersistenceTransaction handle to 05.Application.Behaviors'
    // smaller local seam — the same same-name-different-namespace bridge shape as the outer adapter.
    private sealed class PersistenceTransactionAdapter(PersistenceIPersistenceTransaction inner)
        : AppIPersistenceTransaction
    {
        public Task CommitAsync(CancellationToken cancellationToken) => inner.CommitAsync(cancellationToken);

        public Task RollbackAsync(CancellationToken cancellationToken) => inner.RollbackAsync(cancellationToken);

        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
}

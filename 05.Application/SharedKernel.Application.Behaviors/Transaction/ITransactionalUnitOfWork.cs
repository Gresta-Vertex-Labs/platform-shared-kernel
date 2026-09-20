namespace SharedKernel.Application.Behaviors.Transaction;

/// <summary>
/// An optional capability of <see cref="IUnitOfWork"/>: opening an explicit transaction that spans the
/// remainder of the pipeline, so infrastructure invoked from inside a command handler — most notably
/// an append-only audit-trail writer recording a successful outcome — can enlist in, and commit
/// atomically with, the same physical transaction as the eventual
/// <see cref="IUnitOfWork.SaveChangesAsync"/> call.
/// </summary>
/// <remarks>
/// <para>
/// This is <b>not</b> <c>SharedKernel.Persistence.Abstractions.UnitOfWork.ITransactionalUnitOfWork</c>
/// (<c>06.Persistence</c>) — the same same-name-different-namespace bridge shape as
/// <see cref="IUnitOfWork"/>. A deliberately smaller seam: no isolation-level overloads and no
/// retry-safe <c>ExecuteInTransactionAsync</c> variant — <see cref="TransactionBehavior{TRequest,TResponse}"/>
/// only ever needs "begin, then commit or roll back exactly once," which this single member expresses.
/// </para>
/// <para>
/// An implementation opts into this capability by implementing this interface alongside
/// <see cref="IUnitOfWork"/> on the same type; <see cref="TransactionBehavior{TRequest,TResponse}"/>
/// detects it with a runtime type check against the resolved <see cref="IUnitOfWork"/> instance, so a
/// consumer that registers only the plain <see cref="IUnitOfWork"/> is completely unaffected — nothing
/// about the non-transactional path changes. This package ships only the interface — no implementation.
/// </para>
/// </remarks>
public interface ITransactionalUnitOfWork : IUnitOfWork
{
    /// <summary>
    /// Opens an explicit transaction that stays active until <see cref="IPersistenceTransaction.CommitAsync"/>,
    /// <see cref="IPersistenceTransaction.RollbackAsync"/>, or disposal.
    /// </summary>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>A handle representing the open transaction.</returns>
    Task<IPersistenceTransaction> BeginTransactionAsync(CancellationToken cancellationToken);
}

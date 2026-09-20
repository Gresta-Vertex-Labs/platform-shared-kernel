namespace SharedKernel.Application.Behaviors.Transaction;

/// <summary>
/// A minimal, provider-agnostic transaction handle opened by
/// <see cref="ITransactionalUnitOfWork.BeginTransactionAsync"/>.
/// </summary>
/// <remarks>
/// <para>
/// This is <b>not</b> <c>SharedKernel.Persistence.Abstractions.UnitOfWork.IPersistenceTransaction</c>
/// (<c>06.Persistence</c>). <c>05.Application</c> can never reference <c>06.Persistence</c> (layering
/// runs the other direction), so <see cref="TransactionBehavior{TRequest,TResponse}"/> depends on
/// this local interface; the consuming service bridges it to the real, richer
/// <c>06.Persistence.Abstractions</c> transaction handle at the composition root — the same
/// same-name-different-namespace bridge shape as <see cref="IUnitOfWork"/> and
/// <c>Auditing.IAuditTrailWriter</c>. This package ships only the interface — no implementation.
/// </para>
/// </remarks>
public interface IPersistenceTransaction : IAsyncDisposable
{
    /// <summary>Commits every operation performed within this transaction.</summary>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    Task CommitAsync(CancellationToken cancellationToken);

    /// <summary>Rolls back every operation performed within this transaction.</summary>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <remarks>Safe to call even when nothing has been staged against this transaction yet.</remarks>
    Task RollbackAsync(CancellationToken cancellationToken);
}

namespace SharedKernel.Application.Behaviors.Transaction;

/// <summary>
/// A minimal seam for committing staged mutations after a command handler completes successfully.
/// </summary>
/// <remarks>
/// This is <b>not</b> <c>SharedKernel.Persistence.Abstractions.IUnitOfWork</c>
/// (<c>06.Persistence</c>). <c>05.Application</c> can never reference <c>06.Persistence</c>
/// (layering runs the other direction), so <see cref="TransactionBehavior{TRequest,TResponse}"/>
/// depends on this local interface; the consuming service bridges it to its real persistence
/// <c>IUnitOfWork</c> at the composition root. This package ships only the interface — no
/// implementation. (A future <c>06.Persistence</c> work order may instead have a concrete
/// <c>EfUnitOfWork</c> implement this interface directly, since <c>06.Persistence</c> is permitted
/// to reference <c>05.Application</c>.)
/// </remarks>
public interface IUnitOfWork
{
    /// <summary>Commits all staged mutations.</summary>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The number of state entries written.</returns>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}

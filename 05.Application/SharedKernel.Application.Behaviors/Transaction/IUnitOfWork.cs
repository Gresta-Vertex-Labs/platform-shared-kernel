namespace SharedKernel.Application.Behaviors.Transaction;

/// <summary>
/// A minimal seam for committing staged mutations after a command handler completes successfully.
/// </summary>
/// <remarks>
/// This is <b>not</b> <c>SharedKernel.Persistence.Abstractions.IUnitOfWork</c>
/// (<c>06.Persistence</c>). <c>05.Application</c> can never reference <c>06.Persistence</c>
/// (layering runs the other direction), so <see cref="TransactionBehavior{TRequest,TResponse}"/>
/// depends on this local interface; the consuming service bridges it to its real persistence
/// <c>IUnitOfWork</c> at the composition root (<c>06.Persistence</c> is permitted to reference
/// <c>05.Application</c>, so its own <c>EfUnitOfWork</c> may implement this interface directly).
/// This package ships only the interface — no implementation.
/// </remarks>
public interface IUnitOfWork
{
    /// <summary>Commits all staged mutations.</summary>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The number of state entries written.</returns>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}

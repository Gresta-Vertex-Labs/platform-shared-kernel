using MediatR;
using SharedKernel.Application.Messaging;

namespace SharedKernel.Application.Behaviors.Transaction;

/// <summary>
/// Commits staged mutations via <see cref="IUnitOfWork"/> after the inner pipeline completes.
/// </summary>
/// <typeparam name="TRequest">The command type, constrained to <see cref="ICommandBase"/>.</typeparam>
/// <typeparam name="TResponse">The response type returned by the pipeline.</typeparam>
/// <remarks>
/// Applies to commands only — <see cref="ICommandBase"/> is implemented by
/// <see cref="ICommand"/>/<see cref="ICommand{TResponse}"/>, never by <see cref="IQuery{TResponse}"/>,
/// so this behavior is simply absent from a query's resolved pipeline (a DI-level fact, not a
/// runtime branch). Calls <c>next()</c> then <see cref="IUnitOfWork.SaveChangesAsync"/>, in that
/// order, with no surrounding try/catch: if <c>next()</c> throws, <c>SaveChangesAsync</c> is never
/// reached, so a faulted handler can never commit partial state. Deliberately does not inspect
/// whether the returned <c>Result</c>/<c>Result&lt;T&gt;</c> is success or failure — a
/// <c>Result.Failure</c> is a valid, deliberate handler outcome, not a fault; whether to stage a
/// mutation before returning failure is the handler's own decision.
/// </remarks>
public sealed class TransactionBehavior<TRequest, TResponse>(IUnitOfWork unitOfWork)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : ICommandBase, IRequest<TResponse>
{
    /// <inheritdoc/>
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var response = await next().ConfigureAwait(false);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return response;
    }
}

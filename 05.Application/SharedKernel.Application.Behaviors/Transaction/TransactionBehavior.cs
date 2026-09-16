using MediatR;
using SharedKernel.Application.Behaviors.Commands;
using SharedKernel.Application.Behaviors.Shared;
using SharedKernel.Application.Messaging;

namespace SharedKernel.Application.Behaviors.Transaction;

/// <summary>
/// Commits staged mutations via <see cref="IUnitOfWork"/> after the inner pipeline completes.
/// </summary>
/// <typeparam name="TRequest">The command type, constrained to <see cref="ICommandBase"/>.</typeparam>
/// <typeparam name="TResponse">The response type returned by the pipeline.</typeparam>
/// <remarks>
/// <para>
/// A nested command (<see cref="ICommandScope.IsNested"/>) skips committing entirely and calls
/// <c>next()</c> directly — only the outermost command in a DI scope commits, so a handler that
/// sends further commands via <c>ISender.Send</c> shares one atomic unit of work with its caller.
/// </para>
/// <para>
/// For the outermost command: calls <c>next()</c>, then — only when the response represents a
/// success — calls <see cref="IUnitOfWork.SaveChangesAsync"/>. A <c>Result.Failure</c> commits
/// nothing; a thrown exception never reaches the commit call at all (it propagates out of
/// <c>next()</c> before this behavior's own post-<c>next()</c> code runs).
/// </para>
/// </remarks>
public sealed class TransactionBehavior<TRequest, TResponse>(IUnitOfWork unitOfWork, ICommandScope commandScope)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : ICommandBase, IRequest<TResponse>
{
    /// <inheritdoc/>
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (commandScope.IsNested)
            return await next().ConfigureAwait(false);

        var response = await next().ConfigureAwait(false);

        if (ResponseOutcome.IsSuccess(response))
            await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return response;
    }
}

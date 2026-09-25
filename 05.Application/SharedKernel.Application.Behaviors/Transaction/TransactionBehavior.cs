using MediatR;
using SharedKernel.Application.Behaviors.Commands;
using SharedKernel.Application.Messaging;
using SharedKernel.Execution.Transactions;

namespace SharedKernel.Application.Behaviors.Transaction;

/// <summary>
/// Runs the outermost command's inner pipeline and handler inside one database transaction via
/// <see cref="IUnitOfWork.ExecuteInTransactionAsync{TResult}(Func{CancellationToken,Task{TResult}},CancellationToken)"/>.
/// </summary>
/// <typeparam name="TRequest">The command type, constrained to <see cref="ICommandBase"/>.</typeparam>
/// <typeparam name="TResponse">The response type returned by the pipeline.</typeparam>
/// <remarks>
/// <para>
/// The whole inner pipeline — every inner behavior and the handler — is the transaction's
/// operation. The unit of work saves what it staged, runs every
/// <see cref="IUnitOfWork.OnBeforeCommit"/> callback (the audit trail writes its
/// <c>Succeeded</c> record there) and commits. A failed <c>Result</c> rolls back and commits nothing;
/// an exception rolls back and propagates.
/// </para>
/// <para>
/// <strong>Handlers must be re-runnable.</strong> Because the pipeline runs as one delegate, a
/// retrying execution strategy can replay it after a transient failure: the unit of work discards
/// what the failed attempt staged and this behavior invokes the inner pipeline again. A handler loads
/// what it needs through its repositories on each run and keeps side effects outside the database out
/// of the handler body — queue them with <see cref="ICommandScope.OnCompleted"/>, which runs only after
/// the commit. <see cref="ICommandScope.OnCompleted"/> callbacks queued by a discarded attempt are
/// dropped, so a retried attempt never runs them twice.
/// </para>
/// <para>
/// A command dispatched while a transaction is already active joins it through
/// <c>ExecuteInTransactionAsync</c> — only the outermost command commits, and a joined command that
/// fails (a failed <c>Result</c> or an exception) marks the transaction rollback-only, so its staged
/// changes never commit with the outer command's. A nested command
/// (<see cref="ICommandScope.IsNested"/>) with no active transaction calls <c>next()</c> directly.
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
        if (unitOfWork.IsTransactionActive)
        {
            // Joins: a failure marks the transaction rollback-only instead of committing with the outer command.
            return await unitOfWork.ExecuteInTransactionAsync(_ => next(), cancellationToken).ConfigureAwait(false);
        }

        if (commandScope.IsNested)
            return await next().ConfigureAwait(false);

        // Callbacks queued on this command's frame before the transaction starts (by an outer
        // behavior) survive a retry; anything a discarded attempt queued is dropped before the next.
        var scope = commandScope as CommandScope;
        var callbacksBeforeTransaction = scope?.CurrentFrameCallbackCount ?? 0;

        return await unitOfWork.ExecuteInTransactionAsync(
            async _ =>
            {
                scope?.TruncateCurrentFrame(callbacksBeforeTransaction);
                return await next().ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);
    }
}

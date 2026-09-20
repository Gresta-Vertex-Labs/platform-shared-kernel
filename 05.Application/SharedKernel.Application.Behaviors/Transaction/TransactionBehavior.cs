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
/// For the outermost command, when the resolved <see cref="IUnitOfWork"/> does <b>not</b> also
/// implement <see cref="ITransactionalUnitOfWork"/>: calls <c>next()</c>, then — only when the
/// response represents a success — calls <see cref="IUnitOfWork.SaveChangesAsync"/>. A
/// <c>Result.Failure</c> commits nothing; a thrown exception never reaches the commit call at all (it
/// propagates out of <c>next()</c> before this behavior's own post-<c>next()</c> code runs).
/// </para>
/// <para>
/// For the outermost command, when the resolved <see cref="IUnitOfWork"/> <b>does</b> also implement
/// <see cref="ITransactionalUnitOfWork"/>: opens an explicit transaction via
/// <see cref="ITransactionalUnitOfWork.BeginTransactionAsync"/> <b>before</b> calling <c>next()</c>, so
/// every behavior and handler code that runs during <c>next()</c> — most notably
/// <c>Auditing.AuditingBehavior</c>, registered inner to this behavior in the canonical command stage —
/// observes an already-open transaction. A <c>Result</c> success then calls
/// <see cref="IUnitOfWork.SaveChangesAsync"/> followed by
/// <see cref="IPersistenceTransaction.CommitAsync"/>, so the business write and anything staged
/// against that same transaction — an audit record attesting to this command's success, in particular
/// — commit together, atomically. A <c>Result.Failure</c>, or an exception thrown by either
/// <c>next()</c> or <see cref="IUnitOfWork.SaveChangesAsync"/>, rolls the transaction back instead:
/// nothing staged against it survives. This is what lets an audit-trail writer that requires an
/// already-open ambient transaction to record a successful outcome (so that attestation can never
/// outlive, or precede, the business write it describes) actually have one to enlist in.
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

        if (unitOfWork is ITransactionalUnitOfWork transactionalUnitOfWork)
        {
            return await HandleTransactionallyAsync(transactionalUnitOfWork, next, cancellationToken)
                .ConfigureAwait(false);
        }

        var response = await next().ConfigureAwait(false);

        if (ResponseOutcome.IsSuccess(response))
            await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return response;
    }

    // Opens the transaction BEFORE next() so infrastructure invoked from inside the inner pipeline
    // (AuditingBehavior's success-outcome write, in particular) can enlist in it, then commits or
    // rolls back per the same success/failure/exception rules as the non-transactional path above —
    // see this class's own remarks.
    private static async Task<TResponse> HandleTransactionallyAsync(
        ITransactionalUnitOfWork transactionalUnitOfWork,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        await using var transaction = await transactionalUnitOfWork
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        TResponse response;
        try
        {
            response = await next().ConfigureAwait(false);

            if (ResponseOutcome.IsSuccess(response))
                await transactionalUnitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }

        if (ResponseOutcome.IsSuccess(response))
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        else
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);

        return response;
    }
}

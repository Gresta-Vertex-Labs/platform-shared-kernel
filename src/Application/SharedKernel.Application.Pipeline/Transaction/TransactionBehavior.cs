using System.Diagnostics;
using Microsoft.Extensions.Logging;
using SharedKernel.Application.Commands;
using SharedKernel.Application.Messaging;
using SharedKernel.Application.Pipeline.Commands;
using SharedKernel.Application.Pipeline.Diagnostics;
using SharedKernel.Application.Pipeline.Shared;
using SharedKernel.Application.Pipeline.Tracing;
using SharedKernel.Execution.Transactions;
using SharedKernel.Primitives.Propagation;
using SharedKernel.Primitives.Telemetry;

namespace SharedKernel.Application.Pipeline.Transaction;

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
/// (<see cref="ICommandScope.IsNested"/>) with no active transaction calls <c>next()</c> directly, with no span.
/// </para>
/// <para>
/// <b>Telemetry.</b> The outermost transaction and a joined one each get an <c>application transaction</c> span (tag
/// <c>application.transaction_joined</c>) that counts how often the unit of work ran the delegate
/// (<c>application.attempts</c>). A rollback caused by a failed <c>Result</c> (Warning) or an exception (Error) is
/// logged by the command that owns the transaction, and a replay is logged when the delegate ran more than once.
/// </para>
/// </remarks>
internal sealed class TransactionBehavior<TRequest, TResponse>(
    IUnitOfWork unitOfWork,
    ICommandScope commandScope,
    ILogger<TransactionBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : ICommandBase, IRequest<TResponse>
{
    /// <inheritdoc/>
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerContinuation<TResponse> next,
        CancellationToken cancellationToken)
    {
        var joined = unitOfWork.IsTransactionActive;

        if (!joined && commandScope.IsNested)
            return await next().ConfigureAwait(false);

        var requestType = typeof(TRequest).FullName ?? typeof(TRequest).Name;
        using var activity = ApplicationDiagnostics.ActivitySource.StartActivity(
            ApplicationSpanNames.Transaction,
            ActivityKind.Internal);
        activity?.SetTag(ApplicationTagKeys.TransactionJoined, joined);

        var attempts = 0;

        try
        {
            TResponse response;

            if (joined)
            {
                // Joins: a failure marks the transaction rollback-only instead of committing with the outer command.
                response = await unitOfWork.ExecuteInTransactionAsync(
                    _ =>
                    {
                        attempts++;
                        return next();
                    },
                    cancellationToken).ConfigureAwait(false);
            }
            else
            {
                // Callbacks queued on this command's frame before the transaction starts (by an outer
                // behavior) survive a retry; anything a discarded attempt queued is dropped before the next.
                var scope = commandScope as CommandScope;
                var callbacksBeforeTransaction = scope?.CurrentFrameCallbackCount ?? 0;

                response = await unitOfWork.ExecuteInTransactionAsync(
                    async _ =>
                    {
                        attempts++;
                        scope?.TruncateCurrentFrame(callbacksBeforeTransaction);
                        return await next().ConfigureAwait(false);
                    },
                    cancellationToken).ConfigureAwait(false);
            }

            activity?.SetTag(ApplicationTagKeys.Attempts, attempts);

            var error = ResponseOutcome.TryGetError(response);
            if (error is null)
            {
                activity?.SetTag(WellKnownTagKeys.Outcome, WellKnownOutcomes.Success);
            }
            else
            {
                activity?.SetTag(WellKnownTagKeys.Outcome, activity.RecordFailure(error));
                if (!joined)
                    PipelineLog.TransactionRolledBackByResult(logger, requestType, error.Type.ToString(), error.Code);
            }

            if (attempts > 1)
                PipelineLog.TransactionReplayed(logger, requestType, attempts);

            return response;
        }
        catch (Exception exception)
        {
            activity?.SetTag(ApplicationTagKeys.Attempts, attempts);
            var outcome = activity.RecordException(exception);
            activity?.SetTag(WellKnownTagKeys.Outcome, outcome);

            // A joined command's exception reaches the command that owns the transaction, which logs it once.
            if (!joined && outcome == WellKnownOutcomes.Exception)
                PipelineLog.TransactionRolledBackByException(logger, requestType, exception);

            if (attempts > 1)
                PipelineLog.TransactionReplayed(logger, requestType, attempts);

            throw;
        }
    }
}

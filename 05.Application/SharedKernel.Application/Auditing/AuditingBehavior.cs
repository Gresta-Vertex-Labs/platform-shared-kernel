using MediatR;
using Microsoft.Extensions.Logging;
using SharedKernel.Application.Auditing;

namespace SharedKernel.Application.Pipeline;

/// <summary>
/// Records an append-only audit-trail entry, through <see cref="IAuditTrailWriter"/>, for every
/// outcome of a command that implements <see cref="IAuditableRequest{TResponse}"/>.
/// </summary>
/// <typeparam name="TRequest">
/// The command type, constrained to <see cref="ICommandBase"/> and
/// <see cref="IAuditableRequest{TResponse}"/>.
/// </typeparam>
/// <typeparam name="TResponse">The response type returned by the pipeline.</typeparam>
/// <remarks>
/// <para>
/// <strong>Two halves, one on each side of the transaction.</strong> This behavior runs
/// <em>outside</em> <c>TransactionBehavior</c> and records every failure: a failed <c>Result</c>, a
/// thrown exception, and a failure of the commit itself (a save, a pre-commit callback or the commit
/// throwing). By then the business transaction has rolled back, so the <see cref="AuditOutcome.Failed"/>
/// entry is written on the writer's own connection without waiting on any lock the business
/// transaction held. Its inner partner, registered <em>inside</em> <c>TransactionBehavior</c>,
/// records success: it queues the <see cref="AuditOutcome.Succeeded"/> entry with
/// <see cref="Transactions.IUnitOfWork.OnBeforeCommit"/>, so the entry is written in the same
/// transaction as the change it attests to and commits or rolls back with it. When no transaction is
/// active (no <c>TransactionBehavior</c>), the inner half writes the success entry directly.
/// </para>
/// <para>
/// <see cref="AuditEntry.AfterSnapshot"/> is computed via
/// <see cref="IAuditableRequest{TResponse}.GetAfterSnapshot"/> only on success.
/// <see cref="AuditEntry.ErrorCode"/> carries the response's <c>Error.Code</c> on a failed
/// <c>Result</c>, or the exception's type full name on a fault.
/// </para>
/// <para>
/// <strong>Write failures.</strong> On the failed-<c>Result</c> path an exception from
/// <see cref="IAuditTrailWriter.RecordAsync"/> propagates (fails closed). On the exception path it is
/// logged (EventId 5130) and the <b>original</b> exception still propagates. A failed success write
/// happens inside the transaction, so it rolls the business change back and is then recorded here as a
/// failure.
/// </para>
/// <para>
/// A nested audited command queues its success entry on the outer command's transaction. If the outer
/// command later fails, that entry rolls back with it and no separate failure is recorded for the
/// nested command — the outer command's own entry records the failure.
/// </para>
/// </remarks>
internal sealed partial class AuditingBehavior<TRequest, TResponse>(
    IAuditTrailWriter auditTrailWriter,
    ILogger<AuditingBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : ICommandBase, IAuditableRequest<TResponse>, IRequest<TResponse>
{
    /// <inheritdoc/>
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        TResponse response;
        try
        {
            response = await next().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            var faultEntry = AuditEntries.Failed(request, exception.GetType().FullName ?? exception.GetType().Name);

            try
            {
                await auditTrailWriter.RecordAsync(faultEntry, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception auditWriteException)
            {
                LogAuditWriteFailedDuringException(
                    logger,
                    auditWriteException,
                    typeof(TRequest).FullName ?? typeof(TRequest).Name);
            }

            throw;
        }

        if (ResponseOutcome.TryGetError(response) is { } error)
        {
            await auditTrailWriter
                .RecordAsync(AuditEntries.Failed(request, error.Code), cancellationToken)
                .ConfigureAwait(false);
        }

        return response;
    }

    /// <summary>
    /// <see cref="IAuditTrailWriter.RecordAsync"/> threw while recording the fault entry for a
    /// command whose handler had already thrown (Error). The original handler exception is still
    /// the one propagated to the caller — see this behavior's class remarks.
    /// </summary>
    [LoggerMessage(
        EventId = ApplicationBehaviorsLoggingEventIds.LogAuditWriteFailedDuringException,
        Level = LogLevel.Error,
        Message = "Recording the audit entry for a faulted {RequestType} failed; the original exception still propagates")]
    private static partial void LogAuditWriteFailedDuringException(ILogger logger, Exception exception, string requestType);
}

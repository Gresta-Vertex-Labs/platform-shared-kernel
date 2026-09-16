using MediatR;
using Microsoft.Extensions.Logging;
using SharedKernel.Application.Behaviors.Shared;
using SharedKernel.Application.Messaging;

namespace SharedKernel.Application.Behaviors.Auditing;

/// <summary>
/// Records an explicit, append-only audit-trail entry for an opted-in command via
/// <see cref="IAuditTrailWriter"/>.
/// </summary>
/// <typeparam name="TRequest">
/// The command type, constrained to <see cref="ICommandBase"/> and
/// <see cref="IAuditableRequest{TResponse}"/>.
/// </typeparam>
/// <typeparam name="TResponse">The response type returned by the pipeline.</typeparam>
/// <remarks>
/// <para>
/// Calls <c>next()</c>, then unconditionally records an entry for every outcome — success, a
/// <c>Result.Failure</c>, and a thrown exception alike. A rejected or faulted high-risk attempt is
/// itself often the compliance-relevant event, so leaving it unrecorded would be the wrong default.
/// Runs for nested commands exactly as for the outermost command — a nested audited command still
/// records its own entry.
/// </para>
/// <para>
/// <see cref="AuditEntry.AfterSnapshot"/> is computed via
/// <see cref="IAuditableRequest{TResponse}.GetAfterSnapshot"/> only on success; both a
/// <c>Result.Failure</c> and a thrown exception record <see cref="AuditEntry.Succeeded"/> =
/// <see langword="false"/> with <see cref="AuditEntry.AfterSnapshot"/> left <see langword="null"/>
/// (there is no new state to snapshot). <see cref="AuditEntry.ErrorCode"/> carries the response's
/// <c>Error.Code</c> on a <c>Result.Failure</c>, or the thrown exception's type full name on a fault
/// — the exception itself is then rethrown unchanged after the entry is recorded.
/// </para>
/// <para>
/// On the success/<c>Result.Failure</c> path, an exception thrown by
/// <see cref="IAuditTrailWriter.RecordAsync"/> itself is <b>not</b> caught — it propagates unchanged
/// and blocks the rest of the pipeline (fails closed): a failed audit write means
/// <c>Transaction.TransactionBehavior</c>, which wraps this behavior in the canonical command stage,
/// never reaches its own commit either. On the handler-exception path this fail-closed rule would
/// hide the original fault behind the audit writer's own exception, so it does not apply there: if
/// <see cref="IAuditTrailWriter.RecordAsync"/> itself throws while recording the fault entry, that
/// write failure is logged and the <b>original</b> handler exception is still the one that
/// propagates.
/// </para>
/// </remarks>
public sealed partial class AuditingBehavior<TRequest, TResponse>(
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
            var faultEntry = new AuditEntry(
                request.Action,
                request.ResourceType,
                request.ResourceId,
                request.BeforeSnapshot,
                AfterSnapshot: null,
                Succeeded: false,
                ErrorCode: exception.GetType().FullName ?? exception.GetType().Name);

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

        var error = ResponseOutcome.TryGetError(response);
        var succeeded = error is null;

        var entry = new AuditEntry(
            request.Action,
            request.ResourceType,
            request.ResourceId,
            request.BeforeSnapshot,
            succeeded ? request.GetAfterSnapshot(response) : null,
            succeeded,
            error?.Code);

        await auditTrailWriter.RecordAsync(entry, cancellationToken).ConfigureAwait(false);

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

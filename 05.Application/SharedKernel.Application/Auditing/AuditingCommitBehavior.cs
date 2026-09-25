using MediatR;
using SharedKernel.Application.Auditing;
using SharedKernel.Application.Transactions;

namespace SharedKernel.Application.Pipeline;

/// <summary>
/// The inner half of <see cref="AuditingBehavior{TRequest,TResponse}"/>: records the
/// <see cref="AuditOutcome.Succeeded"/> entry of an audited command inside its transaction.
/// </summary>
/// <remarks>
/// Registered by <c>WithAuditing()</c> immediately inside <c>TransactionBehavior</c>. On a
/// successful response it queues the entry with <see cref="IUnitOfWork.OnBeforeCommit"/> when a
/// transaction is active, so the entry is written after the business changes are saved and commits
/// with them; with no active transaction (or no unit of work registered) it writes the entry directly.
/// Failures are the outer half's job and are ignored here.
/// </remarks>
internal sealed class AuditingCommitBehavior<TRequest, TResponse>(
    IAuditTrailWriter auditTrailWriter,
    IUnitOfWork? unitOfWork = null)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : ICommandBase, IAuditableRequest<TResponse>, IRequest<TResponse>
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var response = await next().ConfigureAwait(false);

        if (!ResponseOutcome.IsSuccess(response))
            return response;

        var entry = AuditEntries.Succeeded(request, response);

        if (unitOfWork is { IsTransactionActive: true })
            unitOfWork.OnBeforeCommit(ct => auditTrailWriter.RecordAsync(entry, ct));
        else
            await auditTrailWriter.RecordAsync(entry, cancellationToken).ConfigureAwait(false);

        return response;
    }
}

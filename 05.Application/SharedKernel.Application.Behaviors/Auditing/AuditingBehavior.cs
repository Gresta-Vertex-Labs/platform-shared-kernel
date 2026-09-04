using MediatR;
using SharedKernel.Application.Behaviors.DualApproval;
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
/// Constrained to <see cref="ICommandBase"/> only — mirrors <c>Transaction.TransactionBehavior</c>'s,
/// <c>Idempotency.IdempotentCommandBehavior</c>'s, and <c>DualApproval.DualApprovalBehavior</c>'s
/// exact commands-only constraint shape; never applies to queries (a DI-level fact, since
/// <c>IQuery&lt;TResponse&gt;</c> never implements <see cref="ICommandBase"/>). No runtime
/// <see langword="is"/>-check for applicability is needed — the .NET DI container's
/// generic-constraint-aware resolution naturally excludes this behavior from any closed
/// <typeparamref name="TRequest"/> that does not satisfy the constraint.
/// </para>
/// <para>
/// Calls <c>next()</c> FIRST to obtain the response, then unconditionally calls
/// <see cref="IAuditTrailWriter.RecordAsync"/> for BOTH a successful and a business-rejected
/// (<c>Result.Failure</c>) outcome — auditing a rejected high-risk attempt is itself often the
/// compliance-relevant event, not just a successful one — but NEVER when <c>next()</c> throws
/// (mirrors <see cref="Logging.ILoggableRequest{TResponse}"/>'s established convention: there is no
/// response object to project). Does NOT catch an exception thrown by
/// <see cref="IAuditTrailWriter.RecordAsync"/> itself — it propagates unchanged and blocks the rest
/// of the pipeline (FAILS CLOSED), consistent with this domain's "log/audit failures are never
/// silently swallowed" convention; a failed audit write means <c>Transaction.TransactionBehavior</c>
/// (which wraps this behavior — see the Pipeline Composition section of
/// <c>05.Application/CLAUDE.md</c>) never reaches its own commit either.
/// </para>
/// <para>
/// <b>Dual-approval linkage:</b> detects <c>request is <see cref="IRequiresDualApproval"/> dual</c>
/// via a plain <see langword="is"/>-pattern (zero coupling to <see cref="IDualApprovalStore"/>
/// itself, zero new project reference) and passes <c>dual.ApprovalKey</c> as
/// <see cref="AuditEntry.ApprovalId"/>; a request implementing only
/// <see cref="IAuditableRequest{TResponse}"/> (no dual-approval) passes <c>ApprovalId = null</c>.
/// </para>
/// <para>
/// <b>AUDIT-WRITE / BUSINESS-COMMIT DECOUPLING (documented, disclosed, not an oversight):</b> since
/// <c>06.Persistence</c>'s real <c>EfAuditTrailWriter</c> is SELF-CONTAINED and calls its own
/// <c>SaveChangesAsync</c> independently of the caller's business <see cref="Transaction.IUnitOfWork"/>,
/// the recorded audit entry reflects the outcome the handler COMPUTED, not a guarantee the business
/// mutation itself later persisted — matching <c>06.Persistence</c>'s own already-locked design
/// decision, not a gap introduced here.
/// </para>
/// <para>
/// Runs TENTH in the canonical pipeline, immediately inside <c>Transaction.TransactionBehavior</c> —
/// see the Pipeline Composition section of <c>05.Application/CLAUDE.md</c>.
/// </para>
/// </remarks>
public sealed class AuditingBehavior<TRequest, TResponse>(IAuditTrailWriter auditTrailWriter)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : ICommandBase, IAuditableRequest<TResponse>, IRequest<TResponse>
{
    /// <inheritdoc/>
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var response = await next().ConfigureAwait(false);

        var approvalId = request is IRequiresDualApproval dual ? dual.ApprovalKey : null;

        var entry = new AuditEntry(
            request.Action,
            request.ResourceType,
            request.ResourceId,
            request.BeforeSnapshot,
            request.GetAfterSnapshot(response),
            approvalId);

        await auditTrailWriter.RecordAsync(entry, cancellationToken).ConfigureAwait(false);

        return response;
    }
}

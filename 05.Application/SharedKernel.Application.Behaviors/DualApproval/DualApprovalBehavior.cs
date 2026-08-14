using MediatR;
using SharedKernel.Application.Behaviors.Authorization;
using SharedKernel.Application.Behaviors.Shared;
using SharedKernel.Application.Messaging;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.Application.Behaviors.DualApproval;

/// <summary>
/// Short-circuits the pipeline unless a second, distinct identity has approved the pending command
/// instance ("maker-checker" / dual-control).
/// </summary>
/// <typeparam name="TRequest">
/// The command type, constrained to <see cref="ICommandBase"/> and <see cref="IRequiresDualApproval"/>.
/// </typeparam>
/// <typeparam name="TResponse">The response type returned by the pipeline.</typeparam>
/// <remarks>
/// <para>
/// Constrained to <see cref="ICommandBase"/> only — mirrors <c>Transaction.TransactionBehavior</c>'s
/// and <c>Idempotency.IdempotentCommandBehavior</c>'s exact commands-only constraint shape; never
/// applies to queries (a DI-level fact, since <c>IQuery&lt;TResponse&gt;</c> never implements
/// <see cref="ICommandBase"/>).
/// </para>
/// <para>
/// Resolves the current caller's identity via the injected <see cref="IAuthorizationContext"/>'s
/// <see cref="IAuthorizationContextIdentity"/> capability, then calls
/// <see cref="IDualApprovalStore.TryGetApprovalAsync"/> for <see cref="IRequiresDualApproval.ApprovalKey"/>:
/// <list type="bullet">
///   <item><description>
///   No recorded approval (<see langword="null"/>) → short-circuits WITHOUT calling <c>next()</c>,
///   returns <c>Result.Failure(Error.Forbidden(...))</c> — "awaiting a second approver."
///   </description></item>
///   <item><description>
///   Recorded approval whose identity equals the resolved initiator identity (SELF-APPROVAL) →
///   short-circuits WITHOUT calling <c>next()</c>, returns <c>Result.Failure(Error.Forbidden(...))</c>.
///   This check is evaluated unconditionally whenever a record exists, so self-approval can never
///   slip through merely because a non-null record is present — STRUCTURALLY IMPOSSIBLE, not merely
///   discouraged: there is no code path in this behavior that calls <c>next()</c> when the two
///   identities match, regardless of how the record was created.
///   </description></item>
///   <item><description>
///   Recorded approval from a DISTINCT identity → calls <c>next()</c>, returns its result unchanged.
///   </description></item>
/// </list>
/// </para>
/// <para>
/// NEVER throws for the awaiting-approval or self-approval cases —
/// <c>Result.Failure(Error.Forbidden(...))</c> is a foreseeable, expected outcome, mirroring
/// <c>Authorization.AuthorizationBehavior</c>'s never-throw contract; the one deliberate behavior
/// short-circuit reserved for a thrown exception remains <c>ValidationException</c>. A registered
/// <see cref="IAuthorizationContext"/> that does NOT additionally implement
/// <see cref="IAuthorizationContextIdentity"/> is a composition-root misconfiguration — not a
/// foreseeable business outcome — and is reported via a thrown <see cref="InvalidOperationException"/>.
/// </para>
/// <para>
/// Does NOT clear, consume, or expire the approval record — record lifecycle (one-time-use
/// invalidation, expiry, re-approval-on-command-change) is the consuming service's own
/// approval-recording workflow's responsibility; this behavior only ever reads. Never calls
/// <see cref="IDualApprovalStore.RecordApprovalAsync"/> — recording an approval is the consuming
/// service's own separate approval-workflow responsibility.
/// </para>
/// <para>
/// Reuses <see cref="FailureResponseFactory"/> for the generic <typeparamref name="TResponse"/>
/// failure construction — the same mechanism <c>Authorization.AuthorizationBehavior</c>/
/// <c>Idempotency.IdempotentCommandBehavior</c> already use, never a third implementation.
/// </para>
/// <para>
/// Runs sixth in the canonical pipeline, immediately after <c>Authorization.AuthorizationBehavior</c>
/// and before <c>Caching.CachingBehavior</c>/<c>Resilience.ResilienceBehavior</c>/
/// <c>Idempotency.IdempotentCommandBehavior</c>/<c>Transaction.TransactionBehavior</c>/
/// <c>CacheInvalidation.CacheInvalidationBehavior</c> — see <c>05.Application/CLAUDE.md</c>'s Pipeline
/// Composition section.
/// </para>
/// </remarks>
public sealed class DualApprovalBehavior<TRequest, TResponse>(
    IAuthorizationContext authorizationContext,
    IDualApprovalStore dualApprovalStore) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : ICommandBase, IRequiresDualApproval, IRequest<TResponse>
{
    /// <inheritdoc/>
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (authorizationContext is not IAuthorizationContextIdentity identityProvider)
        {
            throw new InvalidOperationException(
                $"DualApprovalBehavior requires the registered {nameof(IAuthorizationContext)} to also " +
                $"implement {nameof(IAuthorizationContextIdentity)} so the current caller's identity can " +
                "be resolved for self-approval prevention. Update the composition-root bridge implementation " +
                $"to additionally implement {nameof(IAuthorizationContextIdentity)}.");
        }

        var approverIdentity = await dualApprovalStore
            .TryGetApprovalAsync(request.ApprovalKey, cancellationToken)
            .ConfigureAwait(false);

        if (approverIdentity is null)
        {
            return FailureResponseFactory.Create<TResponse>(
                Error.Forbidden(
                    "dualapproval.awaiting_approval",
                    $"'{request.ApprovalKey}' is awaiting approval from a second, distinct identity."));
        }

        var initiatorIdentity = await identityProvider
            .GetCurrentIdentityAsync(cancellationToken)
            .ConfigureAwait(false);

        if (string.Equals(approverIdentity, initiatorIdentity, StringComparison.Ordinal))
        {
            return FailureResponseFactory.Create<TResponse>(
                Error.Forbidden(
                    "dualapproval.self_approval",
                    $"'{request.ApprovalKey}' cannot be approved by the same identity that initiated it."));
        }

        return await next().ConfigureAwait(false);
    }
}

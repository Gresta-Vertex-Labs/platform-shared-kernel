namespace SharedKernel.Application.Behaviors.Authorization;

/// <summary>
/// An optional capability an <see cref="IAuthorizationContext"/> implementation may additionally
/// provide to expose the identity of the currently-executing caller.
/// </summary>
/// <remarks>
/// <para>
/// This is a NEW, SEPARATE interface — never a breaking modification to the already-published
/// <see cref="IAuthorizationContext"/> contract. Mirrors <c>Idempotency.IIdempotencyResponseStore</c>'s
/// exact precedent as a sibling capability layered onto <c>Idempotency.IIdempotencyKeyStore</c>
/// (WO-039, P-242) — not a fresh pattern.
/// </para>
/// <para>
/// <c>DualApproval.DualApprovalBehavior{TRequest,TResponse}</c> detects this via an
/// <c>is IAuthorizationContextIdentity</c> pattern-match on the injected
/// <see cref="IAuthorizationContext"/> — a standard .NET optional-capability-interface check, not
/// reflection, the same class of check <c>Idempotency.IdempotentCommandBehavior{TRequest,TResponse}</c>
/// already uses for <c>Idempotency.IIdempotencyResponseStore</c>. <see cref="Authorization.AuthorizationBehavior{TRequest,TResponse}"/>
/// itself never needs or uses this interface; it exists solely to support
/// <c>DualApproval.DualApprovalBehavior{TRequest,TResponse}</c>'s self-approval prevention.
/// </para>
/// <para>
/// The identity returned by <see cref="GetCurrentIdentityAsync"/> MUST be the SAME opaque identity
/// string the composition-root bridge uses to identify the current approver when later recording an
/// approval via <c>DualApproval.IDualApprovalStore.RecordApprovalAsync</c> — keeping identity
/// representation consistent across both sides of the maker-checker check is the consuming service's
/// responsibility; this package never interprets, normalizes, or compares identity strings beyond
/// ordinal equality.
/// </para>
/// </remarks>
public interface IAuthorizationContextIdentity
{
    /// <summary>Gets the opaque identity string of the currently-executing caller.</summary>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    Task<string> GetCurrentIdentityAsync(CancellationToken cancellationToken);
}

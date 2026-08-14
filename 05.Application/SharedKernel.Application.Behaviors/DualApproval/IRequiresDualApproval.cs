namespace SharedKernel.Application.Behaviors.DualApproval;

/// <summary>
/// Marks a command as requiring a second, distinct approving identity ("maker-checker" /
/// dual-control) before its handler runs.
/// </summary>
/// <remarks>
/// Self-supplied, mirroring <see cref="Idempotency.IIdempotentRequest.IdempotencyKey"/>'s exact
/// pattern — the command instance alone computes its own key (e.g. a deterministic identifier
/// tying this specific pending change to its approval record; often, but not required to be, the
/// same value as the command's own <see cref="Idempotency.IIdempotentRequest.IdempotencyKey"/> if
/// it also implements <see cref="Idempotency.IIdempotentRequest"/>). Never implemented by a
/// query — <c>IQuery&lt;TResponse&gt;</c> never implements <c>ICommandBase</c>, and dual-control is
/// a mutation-gating concern by definition, mirroring
/// <see cref="Idempotency.IIdempotentRequest"/>'s commands-only scope exactly.
/// </remarks>
public interface IRequiresDualApproval
{
    /// <summary>Gets the approval key identifying this pending change's approval record.</summary>
    string ApprovalKey { get; }
}

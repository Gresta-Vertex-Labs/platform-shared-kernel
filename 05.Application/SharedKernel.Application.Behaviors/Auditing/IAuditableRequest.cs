namespace SharedKernel.Application.Behaviors.Auditing;

/// <summary>
/// Opts a command into an explicit, append-only audit-trail write by
/// <see cref="AuditingBehavior{TRequest,TResponse}"/>.
/// </summary>
/// <typeparam name="TResponse">The response type returned by the command.</typeparam>
/// <remarks>
/// <para>
/// Self-supplied, mirroring <see cref="Logging.ILoggableRequest{TResponse}"/>'s exact
/// shape: <see cref="Action"/>, <see cref="ResourceType"/>, <see cref="ResourceId"/>, and
/// <see cref="BeforeSnapshot"/> are immediate properties, known at request-construction time;
/// <see cref="GetAfterSnapshot"/> is a method, invoked only after <c>next()</c> returns
/// NORMALLY (never on a thrown exception — there is no response to project, the same convention
/// <see cref="Logging.ILoggableRequest{TResponse}.GetLoggableResponseFields"/> already established).
/// </para>
/// <para>
/// All values are OPAQUE, caller-pre-serialized strings — <see cref="AuditingBehavior{TRequest,TResponse}"/>
/// and the injected <see cref="IAuditTrailWriter"/> never parse or diff them, mirroring
/// <c>06.Persistence</c>'s own "opaque snapshot" rule for its richer, real audit-trail contract and
/// <see cref="Idempotency.IIdempotencyResponseStore"/>'s "store persists what it's handed" precedent.
/// Never derived via a reflection-based property walk over an arbitrary <c>TRequest</c>/<c>TResponse</c>.
/// </para>
/// <para>
/// Never implemented by a query — auditing is a mutation-gating, commands-only concern, mirroring
/// every other commands-only marker in this domain (<see cref="Idempotency.IIdempotentRequest"/>,
/// <see cref="DualApproval.IRequiresDualApproval"/>).
/// </para>
/// </remarks>
public interface IAuditableRequest<TResponse>
{
    /// <summary>Gets the audited action identifier (e.g. <c>"customer.address.update"</c>).</summary>
    string Action { get; }

    /// <summary>Gets the type of resource affected by the action (e.g. <c>"Customer"</c>).</summary>
    string ResourceType { get; }

    /// <summary>Gets the identifier of the specific resource instance affected by the action.</summary>
    string ResourceId { get; }

    /// <summary>
    /// Gets the caller-pre-serialized snapshot of the resource's state before the action, or
    /// <see langword="null"/> when there is no meaningful "before" state (e.g. a creation or a
    /// key rotation).
    /// </summary>
    string? BeforeSnapshot { get; }

    /// <summary>
    /// Projects the caller-pre-serialized snapshot of the resource's state after the action, given
    /// the response the inner pipeline produced.
    /// </summary>
    /// <param name="response">The response instance returned by the inner pipeline.</param>
    /// <returns>The after-snapshot, or <see langword="null"/> when there is nothing to record.</returns>
    /// <remarks>
    /// Invoked by <see cref="AuditingBehavior{TRequest,TResponse}"/> only after <c>next()</c>
    /// returns normally — never on a thrown exception, since there is no response to project.
    /// </remarks>
    string? GetAfterSnapshot(TResponse response);
}

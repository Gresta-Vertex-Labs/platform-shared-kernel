namespace SharedKernel.Application.Behaviors.Auditing;

/// <summary>
/// Opts a command into an explicit, append-only audit-trail write by
/// <see cref="AuditingBehavior{TRequest,TResponse}"/>.
/// </summary>
/// <typeparam name="TResponse">The response type returned by the command.</typeparam>
/// <remarks>
/// <para>
/// Self-supplied, mirroring <see cref="Logging.ILoggableRequest{TResponse}"/>'s exact shape:
/// <see cref="Action"/>, <see cref="ResourceType"/>, <see cref="ResourceId"/>, and
/// <see cref="BeforeSnapshot"/> are immediate properties, known at request-construction time;
/// <see cref="GetAfterSnapshot"/> is a method, invoked only after <c>next()</c> returns
/// <b>normally and successfully</b> — never on a thrown exception, and never on a
/// <c>Result.Failure</c> either, since a rejected command produced no new state to snapshot.
/// </para>
/// <para>
/// All values are opaque, caller-pre-serialized strings — <see cref="AuditingBehavior{TRequest,TResponse}"/>
/// and the injected <see cref="IAuditTrailWriter"/> never parse or diff them. Never derived via a
/// reflection-based property walk over an arbitrary <c>TRequest</c>/<c>TResponse</c>.
/// </para>
/// <para>
/// Never implemented by a query — auditing is a mutation-gating, commands-only concern.
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
    /// <see langword="null"/> when there is no meaningful "before" state (e.g. a creation).
    /// </summary>
    string? BeforeSnapshot { get; }

    /// <summary>
    /// Projects the caller-pre-serialized snapshot of the resource's state after the action, given
    /// the response the inner pipeline produced.
    /// </summary>
    /// <param name="response">The response instance returned by the inner pipeline.</param>
    /// <returns>The after-snapshot, or <see langword="null"/> when there is nothing to record.</returns>
    /// <remarks>
    /// Invoked by <see cref="AuditingBehavior{TRequest,TResponse}"/> only on a successful outcome —
    /// never on a thrown exception or a <c>Result.Failure</c>.
    /// </remarks>
    string? GetAfterSnapshot(TResponse response);
}

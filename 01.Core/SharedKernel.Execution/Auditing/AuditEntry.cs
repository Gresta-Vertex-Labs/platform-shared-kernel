namespace SharedKernel.Execution.Auditing;

/// <summary>
/// What the caller knows about an audited action, passed to <see cref="IAuditTrailWriter.RecordAsync"/>.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately carries nothing the writer can resolve for itself: the record id, the actor and actor
/// kind, the tenant, the client/session/impersonator identity (all from
/// <see cref="Context.IRequestContext"/>), the timestamp (from the clock) and the correlation id (from
/// <see cref="System.Diagnostics.Activity"/> baggage) are never caller-supplied, so a caller cannot
/// attribute an entry to someone else or to another time.
/// </para>
/// <para>
/// <see cref="BeforeSnapshot"/> and <see cref="AfterSnapshot"/> are opaque, caller-serialized strings;
/// no writer parses or diffs them.
/// </para>
/// </remarks>
public sealed record AuditEntry
{
    /// <summary>Gets the caller-defined verb or code describing what happened (e.g. <c>"order.approve"</c>).</summary>
    public required string Action { get; init; }

    /// <summary>Gets the type of resource the action concerns (e.g. <c>"Order"</c>).</summary>
    public required string ResourceType { get; init; }

    /// <summary>Gets the caller-stringified identifier of the specific resource instance.</summary>
    public required string ResourceId { get; init; }

    /// <summary>Gets whether the action succeeded or failed. See <see cref="IAuditTrailWriter"/> for how this drives the write.</summary>
    public required AuditOutcome Outcome { get; init; }

    /// <summary>Gets the serialized state before the action, or <see langword="null"/> when not applicable (e.g. a creation).</summary>
    public string? BeforeSnapshot { get; init; }

    /// <summary>
    /// Gets the serialized state after the action, or <see langword="null"/>. Always
    /// <see langword="null"/> for a <see cref="AuditOutcome.Failed"/> entry — a rejected action produced
    /// no new state.
    /// </summary>
    public string? AfterSnapshot { get; init; }

    /// <summary>
    /// Gets the failure's error code — a failed <c>Result</c>'s <c>Error.Code</c>, or a thrown
    /// exception's type full name — or <see langword="null"/> for a <see cref="AuditOutcome.Succeeded"/> entry.
    /// </summary>
    public string? ErrorCode { get; init; }

    /// <summary>
    /// Gets the identifier of the approval this action was gated behind (maker-checker), issued by the
    /// service's own approval flow, if any.
    /// </summary>
    public string? ApprovalId { get; init; }

    /// <summary>
    /// Gets an optional idempotency key. A writer that supports it returns the already-recorded entry
    /// for a retried call with the same key and the same event, instead of appending a duplicate.
    /// </summary>
    public string? IdempotencyKey { get; init; }
}

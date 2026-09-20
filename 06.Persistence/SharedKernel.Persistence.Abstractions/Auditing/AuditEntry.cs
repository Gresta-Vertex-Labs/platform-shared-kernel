namespace SharedKernel.Persistence.Abstractions.Auditing;

/// <summary>
/// The caller-supplied input to <see cref="IAuditTrailWriter.RecordAsync"/>.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately a SMALLER shape than <see cref="AuditRecord"/>: <see cref="AuditRecord.Id"/>,
/// <see cref="AuditRecord.TenantId"/>, <see cref="AuditRecord.ActorId"/>,
/// <see cref="AuditRecord.ActorKind"/>, <see cref="AuditRecord.Sequence"/>,
/// <see cref="AuditRecord.OccurredOn"/>, <see cref="AuditRecord.CorrelationId"/>,
/// <see cref="AuditRecord.HashAlgorithm"/>, <see cref="AuditRecord.SchemaVersion"/>,
/// <see cref="AuditRecord.KeyId"/>, <see cref="AuditRecord.RecordHash"/>, and
/// <see cref="AuditRecord.PreviousRecordHash"/> are ALL resolved/computed by the writer implementation
/// — never caller-supplied. This closes off "a caller fabricates its own audit trail" structurally
/// (there is no property on this type a caller could use to do so), rather than merely by convention.
/// </para>
/// <para>
/// <see cref="AuditRecord.CorrelationId"/> in particular is resolved by the writer from the ambient
/// <see cref="System.Diagnostics.Activity"/> baggage (<c>WellKnownBaggageKeys.CorrelationId</c>,
/// <c>01.Core</c>) — never accepted here — so a caller cannot attribute an entry to a correlation id
/// it did not actually run under.
/// </para>
/// </remarks>
public sealed record AuditEntry
{
    /// <summary>Gets the caller-defined verb/code describing what happened (e.g. <c>"OrderApproved"</c>).</summary>
    public required string Action { get; init; }

    /// <summary>
    /// Gets the type of resource this entry concerns (e.g. <c>"Order"</c>). Together with the current
    /// tenant, this identifies the hash chain this entry is appended to — see <see cref="AuditRecord"/>'s
    /// remarks for the chain-partition rationale.
    /// </summary>
    public required string ResourceType { get; init; }

    /// <summary>Gets the caller-stringified identifier of the specific resource instance.</summary>
    public required string ResourceId { get; init; }

    /// <summary>
    /// Gets the caller-supplied, pre-serialized "before" state, or <see langword="null"/> when not
    /// applicable. Opaque — see <see cref="AuditRecord"/>'s remarks.
    /// </summary>
    public string? BeforeSnapshot { get; init; }

    /// <summary>
    /// Gets the caller-supplied, pre-serialized "after" state, or <see langword="null"/> when not
    /// applicable. Opaque — see <see cref="AuditRecord"/>'s remarks.
    /// </summary>
    public string? AfterSnapshot { get; init; }

    /// <summary>
    /// Gets whether the audited action succeeded or failed — drives the writer's transaction
    /// semantics. See <see cref="IAuditTrailWriter"/>'s remarks for the exact rule.
    /// </summary>
    public required AuditOutcome Outcome { get; init; }

    /// <summary>
    /// Gets the failed action's error code (a <c>Result.Failure</c>'s <c>Error.Code</c>, or a thrown
    /// exception's type full name), or <see langword="null"/> when <see cref="Outcome"/> is
    /// <see cref="AuditOutcome.Succeeded"/>.
    /// </summary>
    public string? ErrorCode { get; init; }

    /// <summary>
    /// Gets the optional maker-checker approval linkage — the identifier, issued by the consuming
    /// service's own approval flow, of the approval this action was gated behind, if any.
    /// </summary>
    public string? ApprovalId { get; init; }

    /// <summary>Gets the OAuth2 client id of the caller, if the action was performed by a machine/service caller.</summary>
    public string? ClientId { get; init; }

    /// <summary>Gets the session identifier of the caller, if the action was performed under a tracked session.</summary>
    public string? SessionId { get; init; }

    /// <summary>
    /// Gets the identifier of the identity this action was performed <em>on behalf of</em> — an
    /// impersonation/support-tooling linkage. <see langword="null"/> when the actor acted on its own
    /// identity.
    /// </summary>
    public string? ImpersonatorId { get; init; }

    /// <summary>
    /// Gets the name of the service that performed the action, for a multi-service deployment where
    /// several services write to the same audit trail.
    /// </summary>
    public string? SourceService { get; init; }

    /// <summary>
    /// Gets an optional caller-supplied idempotency key. When set, a retried <see cref="IAuditTrailWriter.RecordAsync"/>
    /// call with the SAME key (within the same chain) returns the already-persisted
    /// <see cref="AuditRecord"/> instead of appending a duplicate — see <see cref="IAuditTrailWriter"/>'s remarks.
    /// </summary>
    public string? IdempotencyKey { get; init; }
}

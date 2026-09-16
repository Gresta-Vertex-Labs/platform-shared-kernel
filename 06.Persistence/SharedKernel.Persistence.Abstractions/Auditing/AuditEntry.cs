namespace SharedKernel.Persistence.Abstractions.Auditing;

/// <summary>
/// The caller-supplied input to <see cref="IAuditTrailWriter.RecordAsync"/>.
/// </summary>
/// <remarks>
/// WO-071/P-456/D-114. Deliberately a SMALLER shape than <see cref="AuditRecord"/>:
/// <see cref="AuditRecord.Id"/>, <see cref="AuditRecord.TenantId"/>, <see cref="AuditRecord.ActorId"/>,
/// <see cref="AuditRecord.OccurredOn"/>, <see cref="AuditRecord.RecordHash"/>, and
/// <see cref="AuditRecord.PreviousRecordHash"/> are ALL resolved/computed by the writer
/// implementation — never caller-supplied. This closes off "a caller fabricates its own audit trail"
/// structurally (there is no property on this type a caller could use to do so), rather than merely
/// by convention.
/// </remarks>
public sealed record AuditEntry
{
    /// <summary>Gets the caller-defined verb/code describing what happened (e.g. <c>"OrderApproved"</c>).</summary>
    public required string Action { get; init; }

    /// <summary>Gets the type of resource this entry concerns (e.g. <c>"Order"</c>).</summary>
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

    /// <summary>Gets the correlation identifier linking this entry to a broader request/operation, if any.</summary>
    public string? CorrelationId { get; init; }

    /// <summary>
    /// Gets the optional maker-checker approval linkage — the identifier, issued by the consuming
    /// service's own approval flow, of the approval this action was gated behind, if any.
    /// </summary>
    public string? ApprovalId { get; init; }
}

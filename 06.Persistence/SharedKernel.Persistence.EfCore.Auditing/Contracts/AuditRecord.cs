using SharedKernel.Execution.Auditing;
using SharedKernel.Execution.Context;
using SharedKernel.Execution.Tenancy;

namespace SharedKernel.Persistence.EfCore.Auditing;

/// <summary>
/// One entry of the append-only audit ledger, as read back: who did what to which resource, when,
/// with what outcome, and — unless erased — the before/after snapshots.
/// </summary>
/// <remarks>
/// <para>
/// A record is written by <see cref="IAuditTrailWriter"/> with a plain <c>INSERT</c> and is
/// <strong>unsealed</strong> until the background sealer assigns it a position in its chain
/// (<see cref="Sequence"/>) and a keyed MAC. Records are chained per <c>(TenantId, ResourceType)</c>;
/// see <c>AUDIT-FORMAT.md</c> for the exact bytes the MAC covers.
/// </para>
/// <para>
/// The snapshots live in a separate, erasable payload row. The chain commits to
/// <c>SHA-256(salt ‖ payload)</c>, never to the payload itself, so erasing a payload
/// (<see cref="IAuditLedgerMaintenance.ErasePayloadAsync"/>) leaves the chain verifiable;
/// <see cref="PayloadErased"/> is then <see langword="true"/> and both snapshots are
/// <see langword="null"/>.
/// </para>
/// </remarks>
public sealed record AuditRecord
{
    /// <summary>Gets the record identifier (a version 7 UUID).</summary>
    public required Guid Id { get; init; }

    /// <summary>
    /// Gets the tenant whose chain holds this record, or <see langword="null"/> for the system chain
    /// (written only under an explicit system identity or cross-tenant scope).
    /// </summary>
    public TenantId? TenantId { get; init; }

    /// <summary>Gets the resource type — the second half of the chain key.</summary>
    public required string ResourceType { get; init; }

    /// <summary>Gets the identifier of the resource instance the action concerned.</summary>
    public required string ResourceId { get; init; }

    /// <summary>Gets the action code, e.g. <c>"OrderApproved"</c>.</summary>
    public required string Action { get; init; }

    /// <summary>Gets whether the action succeeded or failed.</summary>
    public required AuditOutcome Outcome { get; init; }

    /// <summary>Gets the error code of a failed action, or <see langword="null"/>.</summary>
    public string? ErrorCode { get; init; }

    /// <summary>Gets the acting identity: the caller's user id, or the configured service name when there is none.</summary>
    public required string ActorId { get; init; }

    /// <summary>Gets the kind of actor <see cref="ActorId"/> identifies.</summary>
    public required ActorKind ActorKind { get; init; }

    /// <summary>Gets the caller's OAuth2 client id, if any.</summary>
    public string? ClientId { get; init; }

    /// <summary>Gets the caller's session id, if any.</summary>
    public string? SessionId { get; init; }

    /// <summary>Gets the impersonating identity, if any.</summary>
    public string? ImpersonatorId { get; init; }

    /// <summary>Gets the name of the service that wrote the record.</summary>
    public required string SourceService { get; init; }

    /// <summary>Gets the correlation id from the ambient baggage, if any.</summary>
    public string? CorrelationId { get; init; }

    /// <summary>Gets the W3C trace id (32 lowercase hex characters) of the ambient activity, if any.</summary>
    public string? TraceId { get; init; }

    /// <summary>Gets the maker-checker approval id, if any.</summary>
    public string? ApprovalId { get; init; }

    /// <summary>Gets the idempotency key the entry was written with, if any.</summary>
    public string? IdempotencyKey { get; init; }

    /// <summary>Gets when the record was written, from <c>IClock</c>, truncated to whole microseconds.</summary>
    public required DateTimeOffset OccurredOn { get; init; }

    /// <summary>Gets the "before" snapshot, or <see langword="null"/> when none was supplied or the payload was erased.</summary>
    public string? BeforeSnapshot { get; init; }

    /// <summary>Gets the "after" snapshot, or <see langword="null"/> when none was supplied or the payload was erased.</summary>
    public string? AfterSnapshot { get; init; }

    /// <summary>Gets a value indicating whether the payload (snapshots and salt) was erased.</summary>
    public bool PayloadErased { get; init; }

    /// <summary>Gets the record's position in its chain, or <see langword="null"/> while it is not yet sealed.</summary>
    public long? Sequence { get; init; }

    /// <summary>Gets when the sealer chained the record, or <see langword="null"/> while it is not yet sealed.</summary>
    public DateTimeOffset? SealedOn { get; init; }

    /// <summary>Gets the id of the key that sealed the record, or <see langword="null"/> while it is not yet sealed.</summary>
    public string? KeyId { get; init; }

    /// <summary>Gets a value indicating whether the record has been sealed into its chain.</summary>
    public bool IsSealed => Sequence is not null;
}

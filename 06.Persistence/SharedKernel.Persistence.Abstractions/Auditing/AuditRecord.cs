namespace SharedKernel.Persistence.Abstractions.Auditing;

/// <summary>
/// An immutable, append-only audit trail record: who did what, to which resource, when, and
/// (optionally) what changed — tamper-evident via a per-partition hash chain.
/// </summary>
/// <remarks>
/// <para>
/// WO-071/P-456/D-113. Distinct from <c>AuditInterceptor</c> (<c>SharedKernel.Persistence.EfCore</c>),
/// which only stamps mutable <c>CreatedBy</c>/<c>UpdatedAt</c> shadow columns on ordinary entities and
/// preserves no history. <see cref="AuditRecord"/> is a first-class, independently queryable append-only
/// ledger entry, written exclusively via <see cref="IAuditTrailWriter.RecordAsync"/>.
/// </para>
/// <para>
/// <strong>Structural immutability:</strong> every property is <see langword="init"/>-only — there is
/// no member anywhere on this type, or on <see cref="IAuditTrailWriter"/>, that updates or deletes an
/// existing record. The EF Core implementation (<c>AuditRecordImmutabilityInterceptor</c>,
/// <c>SharedKernel.Persistence.EfCore</c>) additionally enforces this at the ORM save boundary.
/// </para>
/// <para>
/// <strong>Hash chain:</strong> <see cref="RecordHash"/> is a hex SHA-256 digest (via
/// <c>SharedKernel.Cryptography.Hashing.IContentHasher</c>) over this record's own fields, chained to
/// <see cref="PreviousRecordHash"/> — the immediately-prior record's <see cref="RecordHash"/> within the
/// same <c>(TenantId, ResourceType)</c> partition. <see cref="PreviousRecordHash"/> is
/// <see langword="null"/> only for the first record in its partition's chain. Tampering with, or
/// deleting, any record in the chain is detectable via <see cref="IAuditQueryService.VerifyChainIntegrityAsync"/>.
/// </para>
/// <para>
/// <strong><see cref="ActorId"/> format:</strong> reuses the platform's existing audit-string-format
/// convention (<c>AuditInterceptor</c>): <c>userId.ToString("D")</c> for an authenticated human/service
/// identity, or a service-name fallback (e.g. <c>"system"</c>) for unauthenticated/background execution
/// — rather than inventing a new <see cref="Guid"/>-typed actor shape.
/// </para>
/// <para>
/// <strong><see cref="BeforeSnapshot"/>/<see cref="AfterSnapshot"/> are opaque:</strong> the caller
/// pre-serializes these (to any format it chooses); this package never parses, validates, or diffs
/// them — mirrors <c>IRequestIdempotencyStore.CompleteAsync</c>'s "store persists the serialized
/// response it's handed" precedent.
/// </para>
/// </remarks>
public sealed record AuditRecord
{
    /// <summary>Gets the unique identifier of this audit record.</summary>
    public required Guid Id { get; init; }

    /// <summary>
    /// Gets the tenant this record belongs to. <see cref="Guid.Empty"/> mirrors
    /// <c>ITenantProvider</c>'s existing single-tenant/no-tenant sentinel convention.
    /// </summary>
    public required Guid TenantId { get; init; }

    /// <summary>
    /// Gets the identity that performed <see cref="Action"/>, in the platform's existing
    /// audit-string-format convention (see type-level remarks).
    /// </summary>
    public required string ActorId { get; init; }

    /// <summary>Gets the caller-defined verb/code describing what happened (e.g. <c>"OrderApproved"</c>).</summary>
    public required string Action { get; init; }

    /// <summary>Gets the type of resource this record concerns (e.g. <c>"Order"</c>).</summary>
    public required string ResourceType { get; init; }

    /// <summary>
    /// Gets the caller-stringified identifier of the specific resource instance — a plain
    /// <see langword="string"/> because aggregate primary-key types vary
    /// (<see cref="Guid"/>, a strongly-typed ID, <see langword="int"/>, ...).
    /// </summary>
    public required string ResourceId { get; init; }

    /// <summary>
    /// Gets the moment this record was written, resolved by the writer from <c>IClock</c> — never
    /// caller-supplied, preventing a forged timestamp.
    /// </summary>
    public required DateTimeOffset OccurredOn { get; init; }

    /// <summary>
    /// Gets the caller-supplied, pre-serialized "before" state, or <see langword="null"/> when not
    /// applicable. Opaque — see type-level remarks.
    /// </summary>
    public string? BeforeSnapshot { get; init; }

    /// <summary>
    /// Gets the caller-supplied, pre-serialized "after" state, or <see langword="null"/> when not
    /// applicable. Opaque — see type-level remarks.
    /// </summary>
    public string? AfterSnapshot { get; init; }

    /// <summary>Gets the correlation identifier linking this record to a broader request/operation, if any.</summary>
    public string? CorrelationId { get; init; }

    /// <summary>
    /// Gets the optional maker-checker approval linkage — the identifier, issued by the consuming
    /// service's own approval flow, of the approval this action was gated behind, if any.
    /// </summary>
    public string? ApprovalId { get; init; }

    /// <summary>Gets the hex SHA-256 digest of this record's own fields — see type-level remarks.</summary>
    public required string RecordHash { get; init; }

    /// <summary>
    /// Gets the immediately-prior record's <see cref="RecordHash"/> within this record's
    /// <c>(TenantId, ResourceType)</c> partition, or <see langword="null"/> for the first record in
    /// that partition's chain.
    /// </summary>
    public string? PreviousRecordHash { get; init; }
}

using SharedKernel.Persistence.Abstractions.Context;

namespace SharedKernel.Persistence.Abstractions.Auditing;

/// <summary>
/// An immutable, append-only audit trail record: who did what, to which resource, when, and
/// (optionally) what changed — tamper-evident via a per-chain, HMAC-keyed, sequence-ordered hash
/// chain.
/// </summary>
/// <remarks>
/// <para>
/// Distinct from <c>AuditInterceptor</c> (<c>SharedKernel.Persistence.EfCore</c>), which only
/// stamps mutable <c>CreatedBy</c>/<c>UpdatedAt</c> shadow columns on ordinary entities and preserves no
/// history. <see cref="AuditRecord"/> is a first-class, independently queryable append-only ledger
/// entry, written exclusively via <see cref="IAuditTrailWriter.RecordAsync"/>.
/// </para>
/// <para>
/// <strong>Structural immutability:</strong> every property is <see langword="init"/>-only — there is
/// no member anywhere on this type, or on <see cref="IAuditTrailWriter"/>, that updates or deletes an
/// existing record. The EF Core implementation additionally enforces this at both the ORM save
/// boundary (a <c>SaveChanges</c> interceptor rejecting a tracked update/delete) and the raw-SQL
/// boundary (a command interceptor rejecting <c>ExecuteUpdate</c>/<c>ExecuteDelete</c> and any other
/// generated <c>UPDATE</c>/<c>DELETE</c> against the mapped table) — and a PostgreSQL migration helper
/// ships a database-level <c>BEFORE UPDATE/DELETE/TRUNCATE</c> trigger as the mandatory production
/// defense-in-depth layer neither of those two application-level guards can substitute for.
/// </para>
/// <para>
/// <strong>Chain partition:</strong> records are chained per <c>(TenantId, ResourceType)</c> — every
/// audited action for every instance of one resource type, within one tenant, shares one chain. This is
/// a deliberate middle ground: chaining per TENANT ALONE would serialize every write for a busy
/// tenant behind one lock regardless of which unrelated resource type it concerns (a bulk "Order"
/// approval run would contend with an unrelated "Customer" profile edit); chaining per RESOURCE
/// INSTANCE would fragment verification into thousands of tiny, near-useless 1-2-record chains and
/// make "was anything about this resource type ever deleted wholesale" undetectable. A resource
/// instance's own history is retrieved by filtering one <c>(TenantId, ResourceType)</c> chain down to
/// its <see cref="ResourceId"/> — <see cref="IAuditQueryService.GetResourceHistoryAsync"/> does exactly
/// that; the chain itself (and <see cref="Sequence"/>'s monotonicity) still spans the whole resource
/// type.
/// </para>
/// <para>
/// <strong>Sequence, not wall-clock time, orders the chain:</strong> <see cref="Sequence"/> is a
/// per-chain-monotonic, contiguous <see cref="long"/> assigned by the writer under a per-chain lock at
/// append time — never derived from <see cref="OccurredOn"/>. <see cref="IAuditQueryService.VerifyChainIntegrityAsync"/>
/// and its checkpoint-anchored counterpart walk the chain strictly in <see cref="Sequence"/> order, so
/// clock skew between concurrent writers (different machines, different NTP drift) can never produce a
/// false "broken chain" report the way ordering by <see cref="OccurredOn"/> could.
/// </para>
/// <para>
/// <strong>Hash chain:</strong> <see cref="RecordHash"/> is a hex HMAC-SHA256 (<see cref="HashAlgorithm"/>)
/// digest, keyed by <see cref="KeyId"/>'s key (held OUTSIDE this database — see
/// <c>IAuditChainKeyProvider</c>, <c>SharedKernel.Persistence.EfCore.Auditing</c>), over this record's
/// own canonically-encoded fields, chained to <see cref="PreviousRecordHash"/> — the immediately-prior
/// record's <see cref="RecordHash"/> in the same chain (<c>Sequence - 1</c>).
/// <see cref="PreviousRecordHash"/> is <see langword="null"/> only for <see cref="Sequence"/> 1, the
/// first record in its chain. A keyed HMAC (rather than an unkeyed hash) means an attacker with
/// database access alone — but not the out-of-database HMAC key — cannot recompute a valid replacement
/// hash for a tampered or forged record.
/// </para>
/// <para>
/// <strong><see cref="TenantId"/> is nullable, never a <see cref="Guid.Empty"/> sentinel:</strong> a
/// system-attributed action with no tenant in play (a startup seeder, a platform-wide background job)
/// records <see langword="null"/>, distinguishable from a genuine tenant whose id happens to be all
/// zeroes — mirrors <c>ICurrentTenantContext.TenantId</c>'s own fail-closed-nullable shape.
/// </para>
/// <para>
/// <strong><see cref="ActorId"/> format:</strong> reuses the platform's existing audit-string-format
/// convention (<c>AuditInterceptor</c>): <c>userId.ToString("D")</c> for an authenticated human/service
/// identity, or a service-name fallback (e.g. <c>"system"</c>) for unauthenticated/background execution
/// — rather than inventing a new <see cref="Guid"/>-typed actor shape. <see cref="ActorKind"/> carries
/// the structured classification <see cref="ActorId"/>'s string form alone cannot.
/// </para>
/// <para>
/// <strong><see cref="BeforeSnapshot"/>/<see cref="AfterSnapshot"/> are opaque:</strong> the caller
/// pre-serializes these (to any format it chooses); this package never parses, validates, or diffs
/// them — mirrors <c>IRequestIdempotencyStore.CompleteAsync</c>'s "store persists the serialized
/// response it's handed" precedent.
/// </para>
/// <para>
/// <strong><see cref="OccurredOn"/> precision:</strong> truncated to whole microseconds — matching
/// PostgreSQL's native <c>timestamptz</c> precision exactly — before it is stored AND before it enters
/// the hash. Hashing the un-truncated, sub-microsecond .NET <see cref="DateTimeOffset"/> value (which
/// carries 100-nanosecond ticks) against a value that round-trips through Postgres at microsecond
/// precision would make every record's own re-verification spuriously fail — not a tamper, an
/// encoding mismatch between what was hashed and what a re-read produces.
/// </para>
/// </remarks>
public sealed record AuditRecord
{
    /// <summary>Gets the unique identifier of this audit record.</summary>
    public required Guid Id { get; init; }

    /// <summary>
    /// Gets the tenant this record belongs to, or <see langword="null"/> for a system-attributed
    /// action performed outside any tenant's context. See type-level remarks.
    /// </summary>
    public Guid? TenantId { get; init; }

    /// <summary>
    /// Gets the identity that performed <see cref="Action"/>, in the platform's existing
    /// audit-string-format convention (see type-level remarks).
    /// </summary>
    public required string ActorId { get; init; }

    /// <summary>Gets the kind of actor <see cref="ActorId"/> identifies.</summary>
    public required ActorKind ActorKind { get; init; }

    /// <summary>Gets the caller-defined verb/code describing what happened (e.g. <c>"OrderApproved"</c>).</summary>
    public required string Action { get; init; }

    /// <summary>Gets the type of resource this record concerns (e.g. <c>"Order"</c>) — half of the chain partition key.</summary>
    public required string ResourceType { get; init; }

    /// <summary>
    /// Gets the caller-stringified identifier of the specific resource instance — a plain
    /// <see langword="string"/> because aggregate primary-key types vary
    /// (<see cref="Guid"/>, a strongly-typed ID, <see langword="int"/>,...).
    /// </summary>
    public required string ResourceId { get; init; }

    /// <summary>
    /// Gets this record's position in its <c>(TenantId, ResourceType)</c> chain — a contiguous,
    /// 1-based, per-chain-monotonic sequence assigned by the writer at append time. See type-level
    /// remarks.
    /// </summary>
    public required long Sequence { get; init; }

    /// <summary>
    /// Gets the moment this record was written, resolved by the writer from <c>IClock</c> — never
    /// caller-supplied, preventing a forged timestamp — and truncated to microsecond precision. See
    /// type-level remarks.
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

    /// <summary>
    /// Gets the correlation identifier linking this record to a broader request/operation, resolved by
    /// the writer from the ambient <see cref="System.Diagnostics.Activity"/> baggage, or
    /// <see langword="null"/> when none was present.
    /// </summary>
    public string? CorrelationId { get; init; }

    /// <summary>
    /// Gets the optional maker-checker approval linkage — the identifier, issued by the consuming
    /// service's own approval flow, of the approval this action was gated behind, if any.
    /// </summary>
    public string? ApprovalId { get; init; }

    /// <summary>Gets whether the audited action succeeded or failed.</summary>
    public required AuditOutcome Outcome { get; init; }

    /// <summary>
    /// Gets the failed action's error code, or <see langword="null"/> when <see cref="Outcome"/> is
    /// <see cref="AuditOutcome.Succeeded"/>.
    /// </summary>
    public string? ErrorCode { get; init; }

    /// <summary>Gets the OAuth2 client id of the caller, if any.</summary>
    public string? ClientId { get; init; }

    /// <summary>Gets the session identifier of the caller, if any.</summary>
    public string? SessionId { get; init; }

    /// <summary>Gets the identity this action was performed on behalf of (impersonation), if any.</summary>
    public string? ImpersonatorId { get; init; }

    /// <summary>Gets the name of the service that performed the action, if recorded.</summary>
    public string? SourceService { get; init; }

    /// <summary>
    /// Gets the caller-supplied idempotency key that made this specific <see cref="IAuditTrailWriter.RecordAsync"/>
    /// call retry-safe, or <see langword="null"/> when none was supplied.
    /// </summary>
    public string? IdempotencyKey { get; init; }

    /// <summary>Gets the name of the keyed hash algorithm used to compute <see cref="RecordHash"/> (e.g. <c>"HMAC-SHA256"</c>).</summary>
    public required string HashAlgorithm { get; init; }

    /// <summary>Gets the version of the canonical field-encoding scheme used to compute <see cref="RecordHash"/>.</summary>
    public required int SchemaVersion { get; init; }

    /// <summary>Gets the identifier of the out-of-database key that computed <see cref="RecordHash"/>.</summary>
    public required string KeyId { get; init; }

    /// <summary>Gets the hex HMAC-SHA256 digest of this record's own fields — see type-level remarks.</summary>
    public required string RecordHash { get; init; }

    /// <summary>
    /// Gets the immediately-prior record's <see cref="RecordHash"/> in this record's chain
    /// (<see cref="Sequence"/> - 1), or <see langword="null"/> for <see cref="Sequence"/> 1.
    /// </summary>
    public string? PreviousRecordHash { get; init; }
}

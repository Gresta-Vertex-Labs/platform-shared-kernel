using System.ComponentModel.DataAnnotations;
using SharedKernel.Configuration;

namespace SharedKernel.Persistence.EfCore.Auditing.Chain;

/// <summary>
/// Configuration for the audit chain's out-of-database HMAC key and (optional) checkpoint signing.
/// </summary>
/// <remarks>
/// Bound from <see cref="SectionName"/> via <c>.WithAuditTrail(IConfiguration)</c>. The HMAC
/// key never lives in the audit-record table itself — see <c>AuditRecord</c>'s remarks for why a keyed
/// hash (over an unkeyed one) matters.
/// </remarks>
public sealed class AuditChainOptions : ISectionBoundOptions
{
    /// <inheritdoc />
    public static string SectionName => "SharedKernel:Persistence:Auditing";

    /// <summary>
    /// The chain's HMAC key material, Base64-encoded. Required, and must decode to at least 32 bytes
    /// (<c>IHmacSigner</c>'s own minimum). Source this from a secret store, never a checked-in file.
    /// </summary>
    [Required]
    public string? HmacKeyBase64 { get; set; }

    /// <summary>
    /// The identifier recorded as <c>AuditRecord.KeyId</c> for every record written under
    /// <see cref="HmacKeyBase64"/>. Defaults to <c>"default"</c>.
    /// </summary>
    [Required]
    public string KeyId { get; set; } = "default";

    /// <summary>
    /// The <c>01.Core</c> <c>IAsymmetricSignatureService</c> key id used to sign checkpoints, when
    /// <c>.WithAuditChainCheckpoints()</c> is opted into. Ignored otherwise.
    /// </summary>
    public string? CheckpointSigningKeyId { get; set; }

    /// <summary>
    /// The PostgreSQL <c>lock_timeout</c> applied while <c>EfAuditTrailWriter</c> holds its OWN
    /// connection and transaction (the <c>Outcome == Failed</c>, or <c>Outcome == Succeeded</c> with no
    /// ambient transaction, path) and attempts to acquire the per-chain advisory lock or write the
    /// record. Defaults to 5 seconds. <see cref="TimeSpan.Zero"/> disables the timeout (waits
    /// indefinitely) — the pre-fix, unbounded-wait behavior; not recommended.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Bounds a specific, real self-deadlock class: a <c>Succeeded</c>-outcome entry recorded inside an
    /// ambient transaction acquires and HOLDS the per-chain advisory lock for that transaction's entire
    /// remaining lifetime; a <c>Failed</c>-outcome entry recorded on the SAME chain, from the SAME
    /// logical request, before that ambient transaction commits or rolls back, opens its OWN connection
    /// and blocks trying to acquire the SAME lock. Because the ambient transaction is idle-in-transaction
    /// awaiting application code — not itself waiting on a lock — PostgreSQL's own deadlock detector sees
    /// no cycle and never fires; without a bound, the blocked acquisition would hang forever. This
    /// timeout converts that hang into a fast, diagnosable failure instead.
    /// </para>
    /// <para>
    /// Set with <c>SET LOCAL lock_timeout</c> on the writer's own connection/transaction only — it never
    /// affects the caller's ambient transaction or any other connection.
    /// </para>
    /// </remarks>
    public TimeSpan AdvisoryLockTimeout { get; set; } = TimeSpan.FromSeconds(5);
}

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
}

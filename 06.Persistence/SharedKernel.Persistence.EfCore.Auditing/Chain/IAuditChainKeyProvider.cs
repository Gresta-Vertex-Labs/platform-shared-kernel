namespace SharedKernel.Persistence.EfCore.Auditing.Chain;

/// <summary>
/// Resolves the out-of-database HMAC key(s) used to compute and verify an audit chain's hash chain.
/// </summary>
/// <remarks>
/// <para>
/// <c>.WithAuditTrail()</c> registers <see cref="ConfiguredAuditChainKeyProvider"/> (reading
/// <see cref="AuditChainOptions"/>) via <c>TryAddSingleton</c> — a consumer wanting a KMS-backed key
/// (rotation, a remote signer) registers its OWN <see cref="IAuditChainKeyProvider"/> BEFORE calling
/// <c>.WithAuditTrail()</c> to override it, mirroring <c>SharedKernel.Persistence.EfCore.Encryption</c>'s
/// "the consumer registers whichever key provider it wants first" extensibility shape.
/// </para>
/// <para>
/// Key rotation for the audit chain itself is out of scope this phase — see
/// <c>EfAuditTrailWriter</c>'s remarks for why a chain-hash key cannot be rotated the same way a
/// symmetric ENCRYPTION key can (every still-verifiable historical record was hashed under the key
/// that was current when it was written; <see cref="TryGetKey"/> exists so a future verifier can still
/// look up a specific historical key by id even though nothing in this phase writes under more than
/// one).
/// </para>
/// </remarks>
public interface IAuditChainKeyProvider
{
    /// <summary>Gets the key new records are appended and hashed under.</summary>
    AuditChainKey GetCurrentKey();

    /// <summary>Attempts to resolve a specific historical key by id, for re-verifying an older record.</summary>
    /// <param name="keyId">The key id, as recorded on <c>AuditRecord.KeyId</c>.</param>
    /// <param name="key">The resolved key, when found.</param>
    /// <returns><see langword="true"/> when <paramref name="keyId"/> is known.</returns>
    bool TryGetKey(string keyId, out AuditChainKey key);
}

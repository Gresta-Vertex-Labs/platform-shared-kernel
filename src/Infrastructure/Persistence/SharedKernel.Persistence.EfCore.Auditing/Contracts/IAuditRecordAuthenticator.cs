namespace SharedKernel.Persistence.EfCore.Auditing;

/// <summary>
/// Computes and verifies the keyed MAC that seals each ledger record into its chain.
/// </summary>
/// <remarks>
/// <para>
/// The default, <c>KeyringAuditRecordAuthenticator</c>, holds an HMAC-SHA256 keyring from
/// <c>AuditLedgerOptions.Keys</c> and uses <c>01.Core</c>'s <c>IHmacSigner</c>. Every member is
/// asynchronous so an implementation can keep keys in a KMS/HSM that computes MACs remotely (for example
/// AWS KMS <c>GenerateMac</c>/<c>VerifyMac</c>); register it before <c>WithAuditTrail</c> to replace the
/// default.
/// </para>
/// <para>
/// Each key carries an <see cref="AuditKeyDescriptor.Order"/>. Newer keys have higher orders. A chain
/// sealed under order <c>n</c> must never later contain a record sealed under a lower order — the
/// verifier reports that as <see cref="AuditVerificationFailureKind.KeyRegression"/>, which is how a
/// forgery made with a retired, leaked key is caught.
/// </para>
/// </remarks>
public interface IAuditRecordAuthenticator
{
    /// <summary>Gets the MAC algorithm name recorded on every link and bound into the MAC input (e.g. <c>"HMAC-SHA256"</c>).</summary>
    string Algorithm { get; }

    /// <summary>Returns the key new records are sealed under.</summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    ValueTask<AuditKeyDescriptor> GetCurrentKeyAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns the descriptor of <paramref name="keyId"/>, or <see langword="null"/> when the key is not (or no longer) available.</summary>
    /// <param name="keyId">The key id recorded on a link.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    ValueTask<AuditKeyDescriptor?> FindKeyAsync(string keyId, CancellationToken cancellationToken = default);

    /// <summary>Computes the MAC of <paramref name="message"/> under <paramref name="keyId"/>.</summary>
    /// <param name="keyId">The key id.</param>
    /// <param name="message">The AUDITv3 link message.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <exception cref="KeyNotFoundException"><paramref name="keyId"/> is unknown.</exception>
    ValueTask<byte[]> ComputeMacAsync(string keyId, ReadOnlyMemory<byte> message, CancellationToken cancellationToken = default);

    /// <summary>Verifies <paramref name="mac"/> over <paramref name="message"/> under <paramref name="keyId"/> in constant time.</summary>
    /// <param name="keyId">The key id.</param>
    /// <param name="message">The AUDITv3 link message.</param>
    /// <param name="mac">The stored MAC.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <exception cref="KeyNotFoundException"><paramref name="keyId"/> is unknown.</exception>
    ValueTask<bool> VerifyMacAsync(string keyId, ReadOnlyMemory<byte> message, ReadOnlyMemory<byte> mac, CancellationToken cancellationToken = default);
}

/// <summary>Identifies a sealing key and its position in the rotation history.</summary>
/// <param name="Id">The key id recorded on every link sealed under the key.</param>
/// <param name="Order">The rotation order; a newer key has a higher order.</param>
public readonly record struct AuditKeyDescriptor(string Id, int Order);

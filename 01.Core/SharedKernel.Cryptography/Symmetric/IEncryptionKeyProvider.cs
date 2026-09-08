namespace SharedKernel.Cryptography.Symmetric;

/// <summary>
/// Resolves the symmetric key material used by <see cref="ISymmetricEncryptionService"/>.
/// </summary>
/// <remarks>
/// <para>
/// Implemented by the consuming service (Key Vault, environment configuration, secret store, etc.).
/// <c>SharedKernel.Cryptography</c> ships no default implementation and holds no key material of
/// its own — key material must never be hardcoded, embedded in source, or read directly from
/// <see cref="Microsoft.Extensions.Configuration.IConfiguration"/> inside this package.
/// </para>
/// <para>
/// Both members are asynchronous and <see cref="CancellationToken"/>-aware so a genuine
/// network-bound KMS/HSM-backed implementation (Azure Key Vault, AWS KMS, HashiCorp Vault) can be
/// written without a blocking-on-async anti-pattern. A configuration-based or in-memory
/// implementation may still complete synchronously — return an already-completed
/// <see cref="ValueTask{TResult}"/> (e.g. <c>new(value)</c>) — this contract permits asynchronous
/// I/O, it does not require it. An implementation that genuinely never performs blocking I/O may
/// additionally implement <see cref="ISynchronousEncryptionKeyProvider"/> to opt into
/// <see cref="ISymmetricEncryptionService"/>'s retained synchronous members
/// (<c>Encrypt</c>/<c>Decrypt</c>/<c>EncryptToString</c>/<c>DecryptToString</c>), which bridge
/// onto this contract via <c>.GetAwaiter().GetResult()</c> — see
/// <see cref="ISynchronousEncryptionKeyProvider"/> and
/// <see cref="EncryptionKeyProviderCapabilities"/> for the full opt-in contract (P-492/WO-081). A
/// provider that does not implement that marker causes those synchronous members to throw
/// <see cref="NotSupportedException"/> instead of attempting the bridge.
/// </para>
/// <para>
/// <b>BREAKING CHANGE (P-446/WO-068):</b> this interface previously exposed synchronous
/// <c>GetCurrentKey()</c>/<c>GetKey(string)</c> members. Those members have been removed outright
/// — not retained as a parallel overload — and replaced by the two asynchronous members below.
/// Every existing implementer must migrate: <c>GetCurrentKey()</c> → <c>GetCurrentKeyAsync</c>,
/// <c>GetKey(string keyId)</c> → <c>GetKeyAsync(string keyId, ...)</c>. A synchronous, config- or
/// in-memory-backed implementer can migrate mechanically by wrapping its existing return value in
/// <c>new ValueTask&lt;CryptographicKey&gt;(...)</c> — no behavioral change is required for that
/// class of implementer.
/// </para>
/// <para>
/// On an unreachable or unauthorized KMS, an implementation MUST propagate a thrown exception —
/// fail-closed is structural to this contract, never a silent placeholder/no-op key.
/// </para>
/// </remarks>
public interface IEncryptionKeyProvider
{
    /// <summary>
    /// Gets the key that should be used for every new
    /// <see cref="ISymmetricEncryptionService.Encrypt(byte[], byte[])"/> /
    /// <see cref="ISymmetricEncryptionService.EncryptAsync(byte[], byte[], CancellationToken)"/> call.
    /// </summary>
    /// <param name="ct">A token to observe while resolving the current key.</param>
    ValueTask<CryptographicKey> GetCurrentKeyAsync(CancellationToken ct = default);

    /// <summary>
    /// Resolves the key identified by <paramref name="keyId"/>, used to decrypt payloads
    /// encrypted with an older key version.
    /// </summary>
    /// <param name="keyId">The key version identifier, as recorded on <see cref="EncryptedPayload.KeyId"/>.</param>
    /// <param name="ct">A token to observe while resolving the key.</param>
    /// <returns>The matching <see cref="CryptographicKey"/>, or <c>null</c> if the key was retired or is unknown.</returns>
    ValueTask<CryptographicKey?> GetKeyAsync(string keyId, CancellationToken ct = default);
}

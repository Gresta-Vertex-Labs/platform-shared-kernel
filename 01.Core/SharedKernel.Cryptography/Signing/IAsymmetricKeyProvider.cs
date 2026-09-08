using System.Security.Cryptography;

namespace SharedKernel.Cryptography.Signing;

/// <summary>
/// Resolves the asymmetric key pair material used by <see cref="IAsymmetricSignatureService"/>
/// implementations to sign and verify data for a given <c>keyId</c>.
/// </summary>
/// <remarks>
/// <para>
/// Implemented by the consuming service (Key Vault, certificate store, environment configuration,
/// etc.). <c>SharedKernel.Cryptography</c> ships no default implementation and holds no key
/// material of its own — mirrors <see cref="Symmetric.IEncryptionKeyProvider"/> for the
/// asymmetric-signing case.
/// </para>
/// <para>
/// Both members are asynchronous and <see cref="CancellationToken"/>-aware so a genuine
/// network-bound KMS/HSM-backed implementation (Azure Key Vault, AWS KMS, HashiCorp Vault,
/// certificate store requiring an I/O-bound lookup) can be written without a
/// blocking-on-async anti-pattern. A configuration-based or in-memory implementation may still
/// complete synchronously — return an already-completed <see cref="ValueTask{TResult}"/> (e.g.
/// <c>new(rsa)</c>) — this contract permits asynchronous I/O, it does not require it. An
/// implementation that genuinely never performs blocking I/O may additionally implement
/// <see cref="ISynchronousAsymmetricKeyProvider"/> to opt into <see cref="IAsymmetricSignatureService"/>'s
/// retained synchronous members (<c>Sign</c>/<c>Verify</c>), which bridge onto this contract via
/// <c>.GetAwaiter().GetResult()</c> — see <see cref="ISynchronousAsymmetricKeyProvider"/> and
/// <see cref="AsymmetricKeyProviderCapabilities"/> for the full opt-in contract (P-493/WO-081). A
/// provider that does not implement that marker causes those synchronous members to throw
/// <see cref="NotSupportedException"/> instead of attempting the bridge.
/// </para>
/// <para>
/// <b>THE RETURNED <see cref="RSA"/>/<see cref="ECDsa"/> INSTANCE IS NOT CALLER-OWNED.</b>
/// <see cref="Signing.RsaSignatureService"/>/<see cref="Signing.EcdsaSignatureService"/> never
/// dispose the instance handed back from these members — lifecycle ownership (including
/// eventual disposal, if any) stays with the implementing provider, which may return the same
/// cached instance across many calls. A provider that hands out a freshly-created instance on
/// every call and never disposes it will leak; that lifecycle decision belongs entirely to the
/// provider, never to a caller.
/// </para>
/// <para>
/// <b>BREAKING CHANGE (P-493/WO-081):</b> this interface previously exposed synchronous
/// <c>GetRsaKey(string)</c>/<c>GetEcdsaKey(string)</c> members. Those members have been removed
/// outright — not retained as a parallel overload — and replaced by the two asynchronous members
/// below, mirroring P-446/WO-068's <see cref="Symmetric.IEncryptionKeyProvider"/> precedent
/// exactly. Every existing implementer must migrate: <c>GetRsaKey(keyId)</c> →
/// <c>GetRsaKeyAsync(keyId, ct)</c>, <c>GetEcdsaKey(keyId)</c> → <c>GetEcdsaKeyAsync(keyId, ct)</c>.
/// A synchronous, config- or certificate-store-backed implementer can migrate mechanically by
/// wrapping its existing return value in <c>new ValueTask&lt;RSA&gt;(...)</c> /
/// <c>new ValueTask&lt;ECDsa&gt;(...)</c> — no behavioral change is required for that class of
/// implementer.
/// </para>
/// </remarks>
public interface IAsymmetricKeyProvider
{
    /// <summary>
    /// Resolves the RSA key pair identified by <paramref name="keyId"/>.
    /// </summary>
    /// <param name="keyId">The key identifier supplied to <see cref="IAsymmetricSignatureService.Sign(byte[], string)"/> or <see cref="IAsymmetricSignatureService.Verify(byte[], byte[], string)"/>.</param>
    /// <param name="ct">A token to observe while resolving the key.</param>
    /// <returns>An <see cref="RSA"/> instance containing at least the public key (and the private key when signing is required). Not caller-owned — see the type-level remarks.</returns>
    /// <exception cref="KeyNotFoundException">Thrown when <paramref name="keyId"/> does not resolve to a known RSA key. Propagates through the returned <see cref="ValueTask{TResult}"/> exactly as it did from the prior synchronous member.</exception>
    ValueTask<RSA> GetRsaKeyAsync(string keyId, CancellationToken ct = default);

    /// <summary>
    /// Resolves the ECDSA key pair identified by <paramref name="keyId"/>.
    /// </summary>
    /// <param name="keyId">The key identifier supplied to <see cref="IAsymmetricSignatureService.Sign(byte[], string)"/> or <see cref="IAsymmetricSignatureService.Verify(byte[], byte[], string)"/>.</param>
    /// <param name="ct">A token to observe while resolving the key.</param>
    /// <returns>An <see cref="ECDsa"/> instance containing at least the public key (and the private key when signing is required). Not caller-owned — see the type-level remarks.</returns>
    /// <exception cref="KeyNotFoundException">Thrown when <paramref name="keyId"/> does not resolve to a known ECDSA key. Propagates through the returned <see cref="ValueTask{TResult}"/> exactly as it did from the prior synchronous member.</exception>
    ValueTask<ECDsa> GetEcdsaKeyAsync(string keyId, CancellationToken ct = default);
}

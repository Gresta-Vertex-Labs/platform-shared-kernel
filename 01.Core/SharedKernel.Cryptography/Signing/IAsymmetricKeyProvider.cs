using System.Security.Cryptography;

namespace SharedKernel.Cryptography.Signing;

/// <summary>
/// Resolves the asymmetric key pair material used by <see cref="IAsymmetricSignatureService"/>
/// implementations to sign and verify data for a given <c>keyId</c>.
/// </summary>
/// <remarks>
/// Implemented by the consuming service (Key Vault, certificate store, environment configuration,
/// etc.). <c>SharedKernel.Cryptography</c> ships no default implementation and holds no key
/// material of its own — mirrors <see cref="Symmetric.IEncryptionKeyProvider"/> for the
/// asymmetric-signing case.
/// </remarks>
public interface IAsymmetricKeyProvider
{
    /// <summary>
    /// Resolves the RSA key pair identified by <paramref name="keyId"/>.
    /// </summary>
    /// <param name="keyId">The key identifier supplied to <see cref="IAsymmetricSignatureService.Sign(byte[], string)"/> or <see cref="IAsymmetricSignatureService.Verify(byte[], byte[], string)"/>.</param>
    /// <returns>An <see cref="RSA"/> instance containing at least the public key (and the private key when signing is required).</returns>
    /// <exception cref="KeyNotFoundException">Thrown when <paramref name="keyId"/> does not resolve to a known RSA key.</exception>
    RSA GetRsaKey(string keyId);

    /// <summary>
    /// Resolves the ECDSA key pair identified by <paramref name="keyId"/>.
    /// </summary>
    /// <param name="keyId">The key identifier supplied to <see cref="IAsymmetricSignatureService.Sign(byte[], string)"/> or <see cref="IAsymmetricSignatureService.Verify(byte[], byte[], string)"/>.</param>
    /// <returns>An <see cref="ECDsa"/> instance containing at least the public key (and the private key when signing is required).</returns>
    /// <exception cref="KeyNotFoundException">Thrown when <paramref name="keyId"/> does not resolve to a known ECDSA key.</exception>
    ECDsa GetEcdsaKey(string keyId);
}

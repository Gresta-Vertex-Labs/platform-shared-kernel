namespace SharedKernel.Cryptography.Symmetric;

/// <summary>
/// Resolves the symmetric key material used by <see cref="ISymmetricEncryptionService"/>.
/// </summary>
/// <remarks>
/// Implemented by the consuming service (Key Vault, environment configuration, secret store, etc.).
/// <c>SharedKernel.Cryptography</c> ships no default implementation and holds no key material of
/// its own — key material must never be hardcoded, embedded in source, or read directly from
/// <see cref="Microsoft.Extensions.Configuration.IConfiguration"/> inside this package.
/// </remarks>
public interface IEncryptionKeyProvider
{
    /// <summary>
    /// Gets the key that should be used for every new <see cref="ISymmetricEncryptionService.Encrypt(byte[])"/> call.
    /// </summary>
    CryptographicKey GetCurrentKey();

    /// <summary>
    /// Resolves the key identified by <paramref name="keyId"/>, used to decrypt payloads
    /// encrypted with an older key version.
    /// </summary>
    /// <param name="keyId">The key version identifier, as recorded on <see cref="EncryptedPayload.KeyId"/>.</param>
    /// <returns>The matching <see cref="CryptographicKey"/>, or <c>null</c> if the key was retired or is unknown.</returns>
    CryptographicKey? GetKey(string keyId);
}

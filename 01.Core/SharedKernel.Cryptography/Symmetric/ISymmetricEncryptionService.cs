using SharedKernel.Primitives.Results;

namespace SharedKernel.Cryptography.Symmetric;

/// <summary>
/// Encrypts and decrypts arbitrary byte payloads using an authenticated (AEAD) symmetric cipher.
/// </summary>
/// <remarks>
/// Use this service for general-purpose encryption of arbitrary payloads outside an EF Core
/// column (e.g., before publishing to a queue, writing to blob storage, or returning from an
/// API). It is distinct from <c>06.Persistence</c>'s <c>EncryptedValueConverter</c>, which
/// remains the dedicated path for transparent EF Core column-level encryption.
/// </remarks>
public interface ISymmetricEncryptionService
{
    /// <summary>
    /// Encrypts <paramref name="plaintext"/> using the current key from the configured
    /// <see cref="IEncryptionKeyProvider"/>, with a freshly generated random nonce.
    /// </summary>
    /// <param name="plaintext">The bytes to encrypt.</param>
    /// <returns>An <see cref="EncryptedPayload"/> carrying everything needed to later decrypt it.</returns>
    EncryptedPayload Encrypt(byte[] plaintext);

    /// <summary>
    /// Decrypts <paramref name="payload"/>, resolving the decryption key via
    /// <see cref="EncryptedPayload.KeyId"/>.
    /// </summary>
    /// <param name="payload">A previously produced <see cref="EncryptedPayload"/>.</param>
    /// <returns>
    /// A successful <see cref="Result{T}"/> containing the plaintext bytes, or a failed result
    /// with <see cref="SharedKernel.Primitives.Errors.ErrorType.Unexpected"/> if the payload was
    /// tampered with, the wrong key was used, or the <see cref="EncryptedPayload.KeyId"/> is
    /// unknown. This method never lets <see cref="System.Security.Cryptography.CryptographicException"/>
    /// propagate uncaught.
    /// </returns>
    Result<byte[]> Decrypt(EncryptedPayload payload);

    /// <summary>
    /// Convenience helper: UTF-8 encodes <paramref name="plaintext"/>, encrypts it, and packs
    /// the key id, nonce, ciphertext, and tag together into a single self-describing Base64 string.
    /// </summary>
    /// <param name="plaintext">The plaintext string to encrypt.</param>
    /// <returns>A single self-describing Base64-encoded string.</returns>
    string EncryptToString(string plaintext);

    /// <summary>
    /// Convenience inverse of <see cref="EncryptToString(string)"/>.
    /// </summary>
    /// <param name="encoded">A string previously produced by <see cref="EncryptToString(string)"/>.</param>
    /// <returns>A successful <see cref="Result{T}"/> containing the decrypted UTF-8 string, or a failed result on tamper/wrong-key/malformed input.</returns>
    Result<string> DecryptToString(string encoded);
}

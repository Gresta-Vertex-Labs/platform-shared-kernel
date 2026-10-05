using SharedKernel.Primitives.Results;

namespace SharedKernel.Cryptography.Symmetric;

/// <summary>
/// The synchronous counterpart of <see cref="ISymmetricEncryptionService"/>, using keys from an
/// <see cref="ISynchronousEncryptionKeyProvider"/>.
/// </summary>
/// <remarks>
/// For code that cannot await, such as EF Core value converters and message serializers. It never blocks on I/O,
/// because its key provider never performs I/O. Payloads, associated data rules and failures are identical to
/// <see cref="ISymmetricEncryptionService"/>, and each service decrypts what the other encrypts.
/// </remarks>
public interface ISynchronousSymmetricEncryptionService
{
    /// <summary>Encrypts <paramref name="plaintext"/> with the current key and a fresh random nonce.</summary>
    /// <param name="plaintext">The bytes to encrypt.</param>
    /// <param name="associatedData">Bytes to authenticate but not store.</param>
    /// <returns>The payload.</returns>
    EncryptedPayload Encrypt(ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> associatedData);

    /// <summary>Decrypts <paramref name="payload"/> with the key its id names.</summary>
    /// <param name="payload">The payload.</param>
    /// <param name="associatedData">The associated data used to encrypt it.</param>
    /// <returns>The plaintext, or a failure.</returns>
    Result<byte[]> Decrypt(EncryptedPayload payload, ReadOnlySpan<byte> associatedData);

    /// <summary>Encrypts a string as UTF-8 and returns the payload as Base64Url text.</summary>
    /// <param name="plaintext">The text to encrypt.</param>
    /// <param name="associatedData">Bytes to authenticate but not store.</param>
    /// <returns>The encoded payload.</returns>
    string EncryptToString(string plaintext, ReadOnlySpan<byte> associatedData);

    /// <summary>Decrypts text produced by <see cref="EncryptToString"/>.</summary>
    /// <param name="encoded">The encoded payload.</param>
    /// <param name="associatedData">The associated data used to encrypt it.</param>
    /// <returns>The text, or a failure.</returns>
    Result<string> DecryptToString(string encoded, ReadOnlySpan<byte> associatedData);

    /// <summary>Returns whether <paramref name="payload"/> was encrypted with the provider's current key.</summary>
    /// <param name="payload">The payload.</param>
    /// <returns><see langword="true"/> when no re-encryption is needed.</returns>
    bool IsEncryptedWithCurrentKey(EncryptedPayload payload);

    /// <summary>
    /// Decrypts <paramref name="payload"/> and encrypts the plaintext again with the current key. Returns the same
    /// payload when it already uses the current key.
    /// </summary>
    /// <param name="payload">The payload.</param>
    /// <param name="associatedData">The associated data used to encrypt it; the new payload uses the same.</param>
    /// <returns>The payload under the current key, or the decryption failure.</returns>
    Result<EncryptedPayload> ReEncrypt(EncryptedPayload payload, ReadOnlySpan<byte> associatedData);
}

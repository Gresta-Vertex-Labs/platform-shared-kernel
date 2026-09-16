using SharedKernel.Primitives.Results;

namespace SharedKernel.Cryptography.Symmetric;

/// <summary>Encrypts and decrypts with AES-256-GCM using keys from an <see cref="IEncryptionKeyProvider"/>.</summary>
/// <remarks>
/// <para>
/// <b>Associated data.</b> Every call takes associated data: bytes that are authenticated with the ciphertext but not
/// encrypted or stored, such as a row id, tenant id or message type. Decryption succeeds only with the same bytes,
/// so a payload cannot be copied into another row, tenant or message. There is no default: pass
/// <see cref="ReadOnlyMemory{T}.Empty"/> explicitly when nothing identifies the payload's context.
/// </para>
/// <para>
/// <b>Failures.</b> Decryption never throws for bad input. It returns
/// <see cref="CryptographyErrorCodes.MalformedPayload"/> or <see cref="CryptographyErrorCodes.DecryptionFailed"/>
/// (both <see cref="Primitives.Errors.ErrorType.Validation"/>) or <see cref="CryptographyErrorCodes.UnknownKeyId"/>
/// (<see cref="Primitives.Errors.ErrorType.Unexpected"/>). A key of the wrong length or an unreachable key provider
/// throws.
/// </para>
/// <para>
/// <b>Rotation.</b> After the provider's current key changes, old payloads still decrypt with their own key. Use
/// <see cref="IsEncryptedWithCurrentKeyAsync"/> and <see cref="ReEncryptAsync"/> in a background job to move them to
/// the current key before retiring the old one.
/// </para>
/// <para>
/// Code that cannot be asynchronous uses <see cref="ISynchronousSymmetricEncryptionService"/>, which produces and
/// reads the same payloads.
/// </para>
/// </remarks>
public interface ISymmetricEncryptionService
{
    /// <summary>Encrypts <paramref name="plaintext"/> with the current key and a fresh random nonce.</summary>
    /// <param name="plaintext">The bytes to encrypt.</param>
    /// <param name="associatedData">Bytes to authenticate but not store. See the type remarks.</param>
    /// <param name="cancellationToken">A token to cancel the key lookup.</param>
    /// <returns>The payload.</returns>
    ValueTask<EncryptedPayload> EncryptAsync(
        ReadOnlyMemory<byte> plaintext,
        ReadOnlyMemory<byte> associatedData,
        CancellationToken cancellationToken = default);

    /// <summary>Decrypts <paramref name="payload"/> with the key its id names.</summary>
    /// <param name="payload">The payload.</param>
    /// <param name="associatedData">The associated data used to encrypt it.</param>
    /// <param name="cancellationToken">A token to cancel the key lookup.</param>
    /// <returns>The plaintext, or a failure. See the type remarks.</returns>
    ValueTask<Result<byte[]>> DecryptAsync(
        EncryptedPayload payload,
        ReadOnlyMemory<byte> associatedData,
        CancellationToken cancellationToken = default);

    /// <summary>Encrypts a string as UTF-8 and returns the payload as Base64Url text (<see cref="EncryptedPayload.ToString"/>).</summary>
    /// <param name="plaintext">The text to encrypt.</param>
    /// <param name="associatedData">Bytes to authenticate but not store. See the type remarks.</param>
    /// <param name="cancellationToken">A token to cancel the key lookup.</param>
    /// <returns>The encoded payload.</returns>
    ValueTask<string> EncryptToStringAsync(
        string plaintext,
        ReadOnlyMemory<byte> associatedData,
        CancellationToken cancellationToken = default);

    /// <summary>Decrypts text produced by <see cref="EncryptToStringAsync"/>.</summary>
    /// <param name="encoded">The encoded payload.</param>
    /// <param name="associatedData">The associated data used to encrypt it.</param>
    /// <param name="cancellationToken">A token to cancel the key lookup.</param>
    /// <returns>The text, or a failure. See the type remarks.</returns>
    ValueTask<Result<string>> DecryptToStringAsync(
        string encoded,
        ReadOnlyMemory<byte> associatedData,
        CancellationToken cancellationToken = default);

    /// <summary>Returns whether <paramref name="payload"/> was encrypted with the provider's current key.</summary>
    /// <param name="payload">The payload.</param>
    /// <param name="cancellationToken">A token to cancel the key lookup.</param>
    /// <returns><see langword="true"/> when no re-encryption is needed.</returns>
    ValueTask<bool> IsEncryptedWithCurrentKeyAsync(EncryptedPayload payload, CancellationToken cancellationToken = default);

    /// <summary>
    /// Decrypts <paramref name="payload"/> and encrypts the plaintext again with the current key. Returns the same
    /// payload when it already uses the current key.
    /// </summary>
    /// <param name="payload">The payload.</param>
    /// <param name="associatedData">The associated data used to encrypt it; the new payload uses the same.</param>
    /// <param name="cancellationToken">A token to cancel the key lookups.</param>
    /// <returns>The payload under the current key, or the decryption failure.</returns>
    ValueTask<Result<EncryptedPayload>> ReEncryptAsync(
        EncryptedPayload payload,
        ReadOnlyMemory<byte> associatedData,
        CancellationToken cancellationToken = default);
}

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
    /// <remarks>
    /// THIS METHOD BLOCKS THE CALLING THREAD WHILE RESOLVING THE ENCRYPTION KEY: it bridges onto
    /// the asynchronous <see cref="IEncryptionKeyProvider.GetCurrentKeyAsync(CancellationToken)"/>
    /// via <c>.GetAwaiter().GetResult()</c>. THIS IS GENUINELY NON-BLOCKING when the registered
    /// <see cref="IEncryptionKeyProvider"/> resolves synchronously — the configuration-based
    /// default, or a <see cref="CachedEncryptionKeyProvider"/> cache hit — BUT IT BLOCKS A REAL
    /// THREAD when the registered provider is genuinely network-bound (a raw KMS call on a cache
    /// miss). Hot-path/high-throughput callers should prefer
    /// <see cref="EncryptAsync(byte[], CancellationToken)"/> instead.
    /// </remarks>
    EncryptedPayload Encrypt(byte[] plaintext);

    /// <summary>
    /// Asynchronously encrypts <paramref name="plaintext"/> using the current key from the
    /// configured <see cref="IEncryptionKeyProvider"/>, with a freshly generated random nonce.
    /// </summary>
    /// <param name="plaintext">The bytes to encrypt.</param>
    /// <param name="ct">A token to observe while resolving the encryption key.</param>
    /// <returns>An <see cref="EncryptedPayload"/> carrying everything needed to later decrypt it.</returns>
    /// <remarks>
    /// Prefer this overload over <see cref="Encrypt(byte[])"/> on hot paths / high-throughput
    /// call sites — it never blocks a thread while resolving the key, regardless of whether the
    /// registered <see cref="IEncryptionKeyProvider"/> completes synchronously or asynchronously.
    /// </remarks>
    ValueTask<EncryptedPayload> EncryptAsync(byte[] plaintext, CancellationToken ct = default);

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
    /// <remarks>
    /// THIS METHOD BLOCKS THE CALLING THREAD WHILE RESOLVING THE DECRYPTION KEY: it bridges onto
    /// the asynchronous <see cref="IEncryptionKeyProvider.GetKeyAsync(string, CancellationToken)"/>
    /// via <c>.GetAwaiter().GetResult()</c>. THIS IS GENUINELY NON-BLOCKING when the registered
    /// <see cref="IEncryptionKeyProvider"/> resolves synchronously — the configuration-based
    /// default, or a <see cref="CachedEncryptionKeyProvider"/> cache hit — BUT IT BLOCKS A REAL
    /// THREAD when the registered provider is genuinely network-bound (a raw KMS call on a cache
    /// miss). Hot-path/high-throughput callers should prefer
    /// <see cref="DecryptAsync(EncryptedPayload, CancellationToken)"/> instead.
    /// </remarks>
    Result<byte[]> Decrypt(EncryptedPayload payload);

    /// <summary>
    /// Asynchronously decrypts <paramref name="payload"/>, resolving the decryption key via
    /// <see cref="EncryptedPayload.KeyId"/>.
    /// </summary>
    /// <param name="payload">A previously produced <see cref="EncryptedPayload"/>.</param>
    /// <param name="ct">A token to observe while resolving the decryption key.</param>
    /// <returns>
    /// A successful <see cref="Result{T}"/> containing the plaintext bytes, or a failed result
    /// with <see cref="SharedKernel.Primitives.Errors.ErrorType.Unexpected"/> if the payload was
    /// tampered with, the wrong key was used, or the <see cref="EncryptedPayload.KeyId"/> is
    /// unknown. This method never lets <see cref="System.Security.Cryptography.CryptographicException"/>
    /// propagate uncaught.
    /// </returns>
    /// <remarks>
    /// Prefer this overload over <see cref="Decrypt(EncryptedPayload)"/> on hot paths /
    /// high-throughput call sites — it never blocks a thread while resolving the key, regardless
    /// of whether the registered <see cref="IEncryptionKeyProvider"/> completes synchronously or
    /// asynchronously.
    /// </remarks>
    ValueTask<Result<byte[]>> DecryptAsync(EncryptedPayload payload, CancellationToken ct = default);

    /// <summary>
    /// Convenience helper: UTF-8 encodes <paramref name="plaintext"/>, encrypts it, and packs
    /// the key id, nonce, ciphertext, and tag together into a single self-describing Base64 string.
    /// </summary>
    /// <param name="plaintext">The plaintext string to encrypt.</param>
    /// <returns>A single self-describing Base64-encoded string.</returns>
    /// <remarks>
    /// THIS METHOD BLOCKS THE CALLING THREAD WHILE RESOLVING THE ENCRYPTION KEY — see
    /// <see cref="Encrypt(byte[])"/>'s remarks; the same caveat applies here. Prefer
    /// <see cref="EncryptToStringAsync(string, CancellationToken)"/> on hot paths.
    /// </remarks>
    string EncryptToString(string plaintext);

    /// <summary>
    /// Asynchronous convenience helper: UTF-8 encodes <paramref name="plaintext"/>, encrypts it,
    /// and packs the key id, nonce, ciphertext, and tag together into a single self-describing
    /// Base64 string.
    /// </summary>
    /// <param name="plaintext">The plaintext string to encrypt.</param>
    /// <param name="ct">A token to observe while resolving the encryption key.</param>
    /// <returns>A single self-describing Base64-encoded string.</returns>
    ValueTask<string> EncryptToStringAsync(string plaintext, CancellationToken ct = default);

    /// <summary>
    /// Convenience inverse of <see cref="EncryptToString(string)"/>.
    /// </summary>
    /// <param name="encoded">A string previously produced by <see cref="EncryptToString(string)"/>.</param>
    /// <returns>A successful <see cref="Result{T}"/> containing the decrypted UTF-8 string, or a failed result on tamper/wrong-key/malformed input.</returns>
    /// <remarks>
    /// THIS METHOD BLOCKS THE CALLING THREAD WHILE RESOLVING THE DECRYPTION KEY — see
    /// <see cref="Decrypt(EncryptedPayload)"/>'s remarks; the same caveat applies here. Prefer
    /// <see cref="DecryptToStringAsync(string, CancellationToken)"/> on hot paths.
    /// </remarks>
    Result<string> DecryptToString(string encoded);

    /// <summary>
    /// Asynchronous convenience inverse of <see cref="EncryptToStringAsync(string, CancellationToken)"/>.
    /// </summary>
    /// <param name="encoded">A string previously produced by <see cref="EncryptToString(string)"/> or <see cref="EncryptToStringAsync(string, CancellationToken)"/>.</param>
    /// <param name="ct">A token to observe while resolving the decryption key.</param>
    /// <returns>A successful <see cref="Result{T}"/> containing the decrypted UTF-8 string, or a failed result on tamper/wrong-key/malformed input.</returns>
    ValueTask<Result<string>> DecryptToStringAsync(string encoded, CancellationToken ct = default);
}

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
    /// <param name="associatedData">
    /// Additional authenticated data (AAD): bound into the AES-GCM authentication tag but never
    /// encrypted and never persisted inside the returned <see cref="EncryptedPayload"/>. The
    /// caller must be able to reproduce byte-identical bytes at decrypt time (e.g. the owning
    /// row's primary key, a cache key, a message type, a subscription id) — a mismatch fails
    /// authentication exactly like a tampered ciphertext. Pass <see cref="Array.Empty{T}"/>
    /// explicitly when no natural context binding exists; there is no default value, precisely
    /// so a call site is never silently unbound by omission.
    /// </param>
    /// <returns>An <see cref="EncryptedPayload"/> carrying everything needed to later decrypt it.</returns>
    /// <remarks>
    /// THIS METHOD BLOCKS THE CALLING THREAD WHILE RESOLVING THE ENCRYPTION KEY: it bridges onto
    /// the asynchronous <see cref="IEncryptionKeyProvider.GetCurrentKeyAsync(CancellationToken)"/>
    /// via <c>.GetAwaiter().GetResult()</c>. THIS IS GENUINELY NON-BLOCKING when the registered
    /// <see cref="IEncryptionKeyProvider"/> resolves synchronously — the configuration-based
    /// default, or a <see cref="CachedEncryptionKeyProvider"/> cache hit — BUT IT BLOCKS A REAL
    /// THREAD when the registered provider is genuinely network-bound (a raw KMS call on a cache
    /// miss). Hot-path/high-throughput callers should prefer
    /// <see cref="EncryptAsync(byte[], byte[], CancellationToken)"/> instead.
    /// </remarks>
    EncryptedPayload Encrypt(byte[] plaintext, byte[] associatedData);

    /// <summary>
    /// Asynchronously encrypts <paramref name="plaintext"/> using the current key from the
    /// configured <see cref="IEncryptionKeyProvider"/>, with a freshly generated random nonce.
    /// </summary>
    /// <param name="plaintext">The bytes to encrypt.</param>
    /// <param name="associatedData">
    /// Additional authenticated data (AAD) — see <see cref="Encrypt(byte[], byte[])"/>'s
    /// <paramref name="associatedData"/> remarks; the same contract applies here. No default
    /// value — pass <see cref="Array.Empty{T}"/> explicitly when no natural context binding
    /// exists.
    /// </param>
    /// <param name="ct">A token to observe while resolving the encryption key.</param>
    /// <returns>An <see cref="EncryptedPayload"/> carrying everything needed to later decrypt it.</returns>
    /// <remarks>
    /// Prefer this overload over <see cref="Encrypt(byte[], byte[])"/> on hot paths /
    /// high-throughput call sites — it never blocks a thread while resolving the key, regardless
    /// of whether the registered <see cref="IEncryptionKeyProvider"/> completes synchronously or
    /// asynchronously.
    /// </remarks>
    ValueTask<EncryptedPayload> EncryptAsync(byte[] plaintext, byte[] associatedData, CancellationToken ct = default);

    /// <summary>
    /// Decrypts <paramref name="payload"/>, resolving the decryption key via
    /// <see cref="EncryptedPayload.KeyId"/>.
    /// </summary>
    /// <param name="payload">A previously produced <see cref="EncryptedPayload"/>.</param>
    /// <param name="associatedData">
    /// The same additional authenticated data (AAD) bytes supplied to the original
    /// <see cref="Encrypt(byte[], byte[])"/>/<see cref="EncryptAsync(byte[], byte[], CancellationToken)"/>
    /// call, reproduced byte-for-byte. AAD is never persisted inside <see cref="EncryptedPayload"/>,
    /// so the caller alone is responsible for reconstructing it from context available at decrypt
    /// time. A mismatch is indistinguishable from a tampered ciphertext or wrong key — it fails
    /// authentication and this method returns a failed <see cref="Result{T}"/>. No default value
    /// — pass <see cref="Array.Empty{T}"/> explicitly when the original call used no AAD.
    /// </param>
    /// <returns>
    /// A successful <see cref="Result{T}"/> containing the plaintext bytes, or a failed result
    /// with <see cref="SharedKernel.Primitives.Errors.ErrorType.Unexpected"/> if the payload was
    /// tampered with, the wrong key was used, <paramref name="associatedData"/> does not match
    /// what was supplied at encryption time, or the <see cref="EncryptedPayload.KeyId"/> is
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
    /// <see cref="DecryptAsync(EncryptedPayload, byte[], CancellationToken)"/> instead.
    /// </remarks>
    Result<byte[]> Decrypt(EncryptedPayload payload, byte[] associatedData);

    /// <summary>
    /// Asynchronously decrypts <paramref name="payload"/>, resolving the decryption key via
    /// <see cref="EncryptedPayload.KeyId"/>.
    /// </summary>
    /// <param name="payload">A previously produced <see cref="EncryptedPayload"/>.</param>
    /// <param name="associatedData">
    /// Additional authenticated data (AAD) — see <see cref="Decrypt(EncryptedPayload, byte[])"/>'s
    /// <paramref name="associatedData"/> remarks; the same contract applies here. No default
    /// value — pass <see cref="Array.Empty{T}"/> explicitly when the original call used no AAD.
    /// </param>
    /// <param name="ct">A token to observe while resolving the decryption key.</param>
    /// <returns>
    /// A successful <see cref="Result{T}"/> containing the plaintext bytes, or a failed result
    /// with <see cref="SharedKernel.Primitives.Errors.ErrorType.Unexpected"/> if the payload was
    /// tampered with, the wrong key was used, <paramref name="associatedData"/> does not match
    /// what was supplied at encryption time, or the <see cref="EncryptedPayload.KeyId"/> is
    /// unknown. This method never lets <see cref="System.Security.Cryptography.CryptographicException"/>
    /// propagate uncaught.
    /// </returns>
    /// <remarks>
    /// Prefer this overload over <see cref="Decrypt(EncryptedPayload, byte[])"/> on hot paths /
    /// high-throughput call sites — it never blocks a thread while resolving the key, regardless
    /// of whether the registered <see cref="IEncryptionKeyProvider"/> completes synchronously or
    /// asynchronously.
    /// </remarks>
    ValueTask<Result<byte[]>> DecryptAsync(EncryptedPayload payload, byte[] associatedData, CancellationToken ct = default);

    /// <summary>
    /// Convenience helper: UTF-8 encodes <paramref name="plaintext"/>, encrypts it, and packs
    /// the key id, nonce, ciphertext, and tag together into a single self-describing Base64 string.
    /// </summary>
    /// <param name="plaintext">The plaintext string to encrypt.</param>
    /// <param name="associatedData">
    /// Additional authenticated data (AAD) — see <see cref="Encrypt(byte[], byte[])"/>'s
    /// <paramref name="associatedData"/> remarks; the same contract applies here. Never embedded
    /// in the returned string. No default value — pass <see cref="Array.Empty{T}"/> explicitly
    /// when no natural context binding exists.
    /// </param>
    /// <returns>A single self-describing Base64-encoded string.</returns>
    /// <remarks>
    /// THIS METHOD BLOCKS THE CALLING THREAD WHILE RESOLVING THE ENCRYPTION KEY — see
    /// <see cref="Encrypt(byte[], byte[])"/>'s remarks; the same caveat applies here. Prefer
    /// <see cref="EncryptToStringAsync(string, byte[], CancellationToken)"/> on hot paths.
    /// </remarks>
    string EncryptToString(string plaintext, byte[] associatedData);

    /// <summary>
    /// Asynchronous convenience helper: UTF-8 encodes <paramref name="plaintext"/>, encrypts it,
    /// and packs the key id, nonce, ciphertext, and tag together into a single self-describing
    /// Base64 string.
    /// </summary>
    /// <param name="plaintext">The plaintext string to encrypt.</param>
    /// <param name="associatedData">
    /// Additional authenticated data (AAD) — see <see cref="Encrypt(byte[], byte[])"/>'s
    /// <paramref name="associatedData"/> remarks; the same contract applies here. Never embedded
    /// in the returned string. No default value — pass <see cref="Array.Empty{T}"/> explicitly
    /// when no natural context binding exists.
    /// </param>
    /// <param name="ct">A token to observe while resolving the encryption key.</param>
    /// <returns>A single self-describing Base64-encoded string.</returns>
    ValueTask<string> EncryptToStringAsync(string plaintext, byte[] associatedData, CancellationToken ct = default);

    /// <summary>
    /// Convenience inverse of <see cref="EncryptToString(string, byte[])"/>.
    /// </summary>
    /// <param name="encoded">A string previously produced by <see cref="EncryptToString(string, byte[])"/>.</param>
    /// <param name="associatedData">
    /// The same additional authenticated data (AAD) bytes supplied to the original
    /// <see cref="EncryptToString(string, byte[])"/>/<see cref="EncryptToStringAsync(string, byte[], CancellationToken)"/>
    /// call, reproduced byte-for-byte — see <see cref="Decrypt(EncryptedPayload, byte[])"/>'s
    /// remarks. No default value — pass <see cref="Array.Empty{T}"/> explicitly when the original
    /// call used no AAD.
    /// </param>
    /// <returns>A successful <see cref="Result{T}"/> containing the decrypted UTF-8 string, or a failed result on tamper/wrong-key/mismatched-AAD/malformed input.</returns>
    /// <remarks>
    /// THIS METHOD BLOCKS THE CALLING THREAD WHILE RESOLVING THE DECRYPTION KEY — see
    /// <see cref="Decrypt(EncryptedPayload, byte[])"/>'s remarks; the same caveat applies here.
    /// Prefer <see cref="DecryptToStringAsync(string, byte[], CancellationToken)"/> on hot paths.
    /// </remarks>
    Result<string> DecryptToString(string encoded, byte[] associatedData);

    /// <summary>
    /// Asynchronous convenience inverse of <see cref="EncryptToStringAsync(string, byte[], CancellationToken)"/>.
    /// </summary>
    /// <param name="encoded">A string previously produced by <see cref="EncryptToString(string, byte[])"/> or <see cref="EncryptToStringAsync(string, byte[], CancellationToken)"/>.</param>
    /// <param name="associatedData">
    /// Additional authenticated data (AAD) — see <see cref="DecryptToString(string, byte[])"/>'s
    /// <paramref name="associatedData"/> remarks; the same contract applies here. No default
    /// value — pass <see cref="Array.Empty{T}"/> explicitly when the original call used no AAD.
    /// </param>
    /// <param name="ct">A token to observe while resolving the decryption key.</param>
    /// <returns>A successful <see cref="Result{T}"/> containing the decrypted UTF-8 string, or a failed result on tamper/wrong-key/mismatched-AAD/malformed input.</returns>
    ValueTask<Result<string>> DecryptToStringAsync(string encoded, byte[] associatedData, CancellationToken ct = default);
}

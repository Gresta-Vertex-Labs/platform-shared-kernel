namespace SharedKernel.Cryptography.Symmetric;

/// <summary>Resolves the keys <see cref="ISymmetricEncryptionService"/> encrypts and decrypts with.</summary>
/// <remarks>
/// <para>
/// Asynchronous so that a key management service (Azure Key Vault, AWS KMS, HashiCorp Vault) can be called without
/// blocking a thread. A provider whose keys are already in memory completes synchronously and should also implement
/// <see cref="ISynchronousEncryptionKeyProvider"/>; <see cref="StaticEncryptionKeyProvider"/> does both.
/// </para>
/// <para>
/// Fail closed: when the key service is unreachable or denies access, throw. Never return a placeholder key.
/// </para>
/// </remarks>
public interface IEncryptionKeyProvider
{
    /// <summary>Gets the key new payloads are encrypted with.</summary>
    /// <param name="cancellationToken">A token to cancel the lookup.</param>
    /// <returns>The current key.</returns>
    ValueTask<CryptographicKey> GetCurrentKeyAsync(CancellationToken cancellationToken = default);

    /// <summary>Gets the key with id <paramref name="keyId"/>, current or retired, to decrypt an existing payload.</summary>
    /// <param name="keyId">
    /// The key id read from a payload. It may come from untrusted input, so an implementation must not let arbitrary
    /// ids trigger unbounded work or remote calls.
    /// </param>
    /// <param name="cancellationToken">A token to cancel the lookup.</param>
    /// <returns>The key, or <see langword="null"/> when no key has that id.</returns>
    ValueTask<CryptographicKey?> GetKeyAsync(string keyId, CancellationToken cancellationToken = default);
}

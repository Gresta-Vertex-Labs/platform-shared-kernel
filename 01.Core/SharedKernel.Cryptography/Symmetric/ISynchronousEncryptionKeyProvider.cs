namespace SharedKernel.Cryptography.Symmetric;

/// <summary>
/// Resolves keys without I/O, for <see cref="ISynchronousSymmetricEncryptionService"/> in code paths that cannot be
/// asynchronous, such as EF Core value converters and message serializers.
/// </summary>
/// <remarks>
/// Implement this only when every lookup is answered from memory. A provider that calls a key management service
/// must implement <see cref="IEncryptionKeyProvider"/> instead; to use such keys from synchronous code, load them
/// into memory ahead of time, for example with a hosted service that refreshes a <see cref="StaticEncryptionKeyProvider"/>.
/// </remarks>
public interface ISynchronousEncryptionKeyProvider
{
    /// <summary>Gets the key new payloads are encrypted with.</summary>
    /// <returns>The current key.</returns>
    CryptographicKey GetCurrentKey();

    /// <summary>Gets the key with id <paramref name="keyId"/>, current or retired, to decrypt an existing payload.</summary>
    /// <param name="keyId">The key id read from a payload. It may come from untrusted input.</param>
    /// <returns>The key, or <see langword="null"/> when no key has that id.</returns>
    CryptographicKey? GetKey(string keyId);
}

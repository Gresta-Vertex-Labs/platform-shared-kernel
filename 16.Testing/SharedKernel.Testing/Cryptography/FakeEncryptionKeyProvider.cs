using System.Collections.Concurrent;
using System.Security.Cryptography;
using SharedKernel.Cryptography.Symmetric;

namespace SharedKernel.Testing.Cryptography;

/// <summary>
/// An in-memory key provider for tests, usable from synchronous and asynchronous code, with random 32-byte keys
/// that can be added, rotated and removed while a test runs.
/// </summary>
/// <remarks>
/// Use <see cref="FakeRemoteEncryptionKeyProvider"/> instead to prove that code under test works with a provider
/// that only completes asynchronously, as a key management service does.
/// </remarks>
public sealed class FakeEncryptionKeyProvider : IEncryptionKeyProvider, ISynchronousEncryptionKeyProvider
{
    private readonly ConcurrentDictionary<string, CryptographicKey> _keys = new(StringComparer.Ordinal);
    private volatile string _currentKeyId;

    /// <summary>Creates the provider with one random key, which is current.</summary>
    /// <param name="currentKeyId">The id of the initial key.</param>
    public FakeEncryptionKeyProvider(string currentKeyId = "v1")
    {
        AddKey(currentKeyId);
        _currentKeyId = currentKeyId;
    }

    /// <summary>The id of the current key.</summary>
    public string CurrentKeyId => _currentKeyId;

    /// <summary>Adds a key with random material, replacing any key with the same id.</summary>
    /// <param name="keyId">The key id.</param>
    /// <returns>The key.</returns>
    public CryptographicKey AddKey(string keyId)
    {
        var key = new CryptographicKey(keyId, RandomNumberGenerator.GetBytes(32));
        _keys[keyId] = key;
        return key;
    }

    /// <summary>Makes an existing key current, as a rotation would.</summary>
    /// <param name="keyId">The key id.</param>
    /// <exception cref="System.Collections.Generic.KeyNotFoundException">No key has <paramref name="keyId"/>.</exception>
    public void SetCurrentKey(string keyId)
    {
        if (!_keys.ContainsKey(keyId))
        {
            throw new System.Collections.Generic.KeyNotFoundException($"No fake key has the id '{keyId}'.");
        }

        _currentKeyId = keyId;
    }

    /// <summary>Removes a key, as retiring it would. Payloads encrypted with it then fail with an unknown key id.</summary>
    /// <param name="keyId">The key id.</param>
    public void RemoveKey(string keyId) => _keys.TryRemove(keyId, out _);

    /// <inheritdoc />
    public CryptographicKey GetCurrentKey() => _keys[_currentKeyId];

    /// <inheritdoc />
    public CryptographicKey? GetKey(string keyId) => _keys.GetValueOrDefault(keyId);

    /// <inheritdoc />
    public ValueTask<CryptographicKey> GetCurrentKeyAsync(CancellationToken cancellationToken = default) => new(GetCurrentKey());

    /// <inheritdoc />
    public ValueTask<CryptographicKey?> GetKeyAsync(string keyId, CancellationToken cancellationToken = default) => new(GetKey(keyId));
}

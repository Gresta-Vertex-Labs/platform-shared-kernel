using System.Collections.Concurrent;
using System.Security.Cryptography;
using SharedKernel.Cryptography.Symmetric;

namespace SharedKernel.Testing.Cryptography;

/// <summary>
/// An in-memory key provider that always completes asynchronously, standing in for a key management service in
/// tests. It deliberately does not implement <see cref="ISynchronousEncryptionKeyProvider"/>.
/// </summary>
public sealed class FakeRemoteEncryptionKeyProvider : IEncryptionKeyProvider
{
    private readonly ConcurrentDictionary<string, CryptographicKey> _keys = new(StringComparer.Ordinal);
    private int _currentKeyCalls;
    private int _keyCalls;
    private volatile string _currentKeyId;

    /// <summary>Creates the provider with one random key, which is current.</summary>
    /// <param name="currentKeyId">The id of the initial key.</param>
    public FakeRemoteEncryptionKeyProvider(string currentKeyId = "v1")
    {
        AddKey(currentKeyId);
        _currentKeyId = currentKeyId;
    }

    /// <summary>How many times <see cref="GetCurrentKeyAsync"/> has been called.</summary>
    public int CurrentKeyCallCount => _currentKeyCalls;

    /// <summary>How many times <see cref="GetKeyAsync"/> has been called.</summary>
    public int KeyCallCount => _keyCalls;

    /// <summary>Adds a key with random material, replacing any key with the same id.</summary>
    /// <param name="keyId">The key id.</param>
    /// <returns>The key.</returns>
    public CryptographicKey AddKey(string keyId)
    {
        var key = new CryptographicKey(keyId, RandomNumberGenerator.GetBytes(32));
        _keys[keyId] = key;
        return key;
    }

    /// <summary>Makes an existing key current.</summary>
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

    /// <summary>Removes a key.</summary>
    /// <param name="keyId">The key id.</param>
    public void RemoveKey(string keyId) => _keys.TryRemove(keyId, out _);

    /// <inheritdoc />
    public async ValueTask<CryptographicKey> GetCurrentKeyAsync(CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _currentKeyCalls);
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();
        return _keys[_currentKeyId];
    }

    /// <inheritdoc />
    public async ValueTask<CryptographicKey?> GetKeyAsync(string keyId, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _keyCalls);
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();
        return _keys.GetValueOrDefault(keyId);
    }
}

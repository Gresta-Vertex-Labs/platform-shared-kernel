namespace SharedKernel.Cryptography.Symmetric;

/// <summary>A key provider over a fixed set of in-memory keys, usable from both synchronous and asynchronous code.</summary>
/// <remarks>
/// Use it for keys loaded at startup from configuration or a secret store. To rotate, add the new key, make it
/// current, and keep the old key until no stored payload uses it.
/// </remarks>
/// <example>
/// <code>
/// services.AddSingleton(new StaticEncryptionKeyProvider(
///     currentKeyId: "2026-09",
///     [new CryptographicKey("2026-03", oldKeyBytes), new CryptographicKey("2026-09", newKeyBytes)]));
/// services.AddSingleton&lt;IEncryptionKeyProvider&gt;(sp => sp.GetRequiredService&lt;StaticEncryptionKeyProvider&gt;());
/// services.AddSingleton&lt;ISynchronousEncryptionKeyProvider&gt;(sp => sp.GetRequiredService&lt;StaticEncryptionKeyProvider&gt;());
/// </code>
/// </example>
public sealed class StaticEncryptionKeyProvider : IEncryptionKeyProvider, ISynchronousEncryptionKeyProvider
{
    private readonly Dictionary<string, CryptographicKey> _keys;
    private readonly CryptographicKey _current;

    /// <summary>Creates the provider.</summary>
    /// <param name="currentKeyId">The id of the key new payloads use. Must be one of <paramref name="keys"/>.</param>
    /// <param name="keys">Every key that may still be needed to decrypt. Ids must be unique.</param>
    /// <exception cref="ArgumentException">
    /// <paramref name="keys"/> is empty, contains a duplicate id, or has no key with <paramref name="currentKeyId"/>.
    /// </exception>
    public StaticEncryptionKeyProvider(string currentKeyId, IEnumerable<CryptographicKey> keys)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currentKeyId);
        ArgumentNullException.ThrowIfNull(keys);

        _keys = new Dictionary<string, CryptographicKey>(StringComparer.Ordinal);
        foreach (CryptographicKey key in keys)
        {
            ArgumentNullException.ThrowIfNull(key, nameof(keys));
            if (!_keys.TryAdd(key.Id, key))
            {
                throw new ArgumentException($"More than one key has the id '{key.Id}'.", nameof(keys));
            }
        }

        if (!_keys.TryGetValue(currentKeyId, out CryptographicKey? current))
        {
            throw new ArgumentException($"No key has the current key id '{currentKeyId}'.", nameof(currentKeyId));
        }

        _current = current;
    }

    /// <inheritdoc />
    public CryptographicKey GetCurrentKey() => _current;

    /// <inheritdoc />
    public CryptographicKey? GetKey(string keyId)
    {
        ArgumentNullException.ThrowIfNull(keyId);
        return _keys.GetValueOrDefault(keyId);
    }

    /// <inheritdoc />
    public ValueTask<CryptographicKey> GetCurrentKeyAsync(CancellationToken cancellationToken = default) => new(_current);

    /// <inheritdoc />
    public ValueTask<CryptographicKey?> GetKeyAsync(string keyId, CancellationToken cancellationToken = default) =>
        new(GetKey(keyId));
}

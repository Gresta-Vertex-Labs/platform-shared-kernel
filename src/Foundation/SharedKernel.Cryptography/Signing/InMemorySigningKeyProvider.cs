namespace SharedKernel.Cryptography.Signing;

/// <summary>A signing key provider over a fixed set of keys held in memory.</summary>
/// <remarks>Disposing the provider disposes its keys.</remarks>
/// <example>
/// <code>
/// var provider = new InMemorySigningKeyProvider(
/// [
///     SigningKey.FromECDsa("webhooks-2026", ECDsa.Create(ECCurve.NamedCurves.nistP256)),
/// ]);
/// services.AddSingleton&lt;ISigningKeyProvider&gt;(provider);
/// </code>
/// </example>
public sealed class InMemorySigningKeyProvider : ISigningKeyProvider, IDisposable
{
    private readonly Dictionary<string, SigningKey> _keys;

    /// <summary>Creates the provider.</summary>
    /// <param name="keys">The keys. Ids must be unique.</param>
    /// <exception cref="ArgumentException">Two keys share an id.</exception>
    public InMemorySigningKeyProvider(IEnumerable<SigningKey> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);

        _keys = new Dictionary<string, SigningKey>(StringComparer.Ordinal);
        foreach (SigningKey key in keys)
        {
            ArgumentNullException.ThrowIfNull(key, nameof(keys));
            if (!_keys.TryAdd(key.KeyId, key))
            {
                throw new ArgumentException($"More than one signing key has the id '{key.KeyId}'.", nameof(keys));
            }
        }
    }

    /// <inheritdoc />
    public ValueTask<SigningKey?> GetSigningKeyAsync(string keyId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(keyId);
        return new(_keys.GetValueOrDefault(keyId));
    }

    /// <summary>Disposes every key.</summary>
    public void Dispose()
    {
        foreach (SigningKey key in _keys.Values)
        {
            key.Dispose();
        }
    }
}

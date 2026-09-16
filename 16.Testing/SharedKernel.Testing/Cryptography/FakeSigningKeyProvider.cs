using System.Collections.Concurrent;
using System.Security.Cryptography;
using SharedKernel.Cryptography.Signing;

namespace SharedKernel.Testing.Cryptography;

/// <summary>An in-memory <see cref="ISigningKeyProvider"/> that creates real keys for tests.</summary>
/// <remarks>
/// Keys are created with <see cref="AddKey"/>, or on first request with <see cref="DefaultAlgorithm"/> when
/// <see cref="CreateKeysOnDemand"/> is <see langword="true"/>. Disposing the provider disposes its keys.
/// </remarks>
public sealed class FakeSigningKeyProvider : ISigningKeyProvider, IDisposable
{
    private readonly ConcurrentDictionary<string, SigningKey> _keys = new(StringComparer.Ordinal);

    /// <summary>Whether an unknown key id creates a key. Defaults to <see langword="true"/>.</summary>
    public bool CreateKeysOnDemand { get; set; } = true;

    /// <summary>The algorithm of keys created on demand. Defaults to <see cref="SignatureAlgorithm.ES256"/>.</summary>
    public SignatureAlgorithm DefaultAlgorithm { get; set; } = SignatureAlgorithm.ES256;

    /// <summary>Creates a key with a fresh key pair, replacing any key with the same id.</summary>
    /// <param name="keyId">The key id.</param>
    /// <param name="algorithm">The algorithm.</param>
    /// <returns>The key.</returns>
    public SigningKey AddKey(string keyId, SignatureAlgorithm algorithm)
    {
        SigningKey key = Create(keyId, algorithm);
        if (_keys.TryGetValue(keyId, out SigningKey? previous))
        {
            previous.Dispose();
        }

        _keys[keyId] = key;
        return key;
    }

    /// <inheritdoc />
    public ValueTask<SigningKey?> GetSigningKeyAsync(string keyId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(keyId);

        if (_keys.TryGetValue(keyId, out SigningKey? key))
        {
            return new(key);
        }

        if (!CreateKeysOnDemand)
        {
            return new((SigningKey?)null);
        }

        // GetOrAdd may run its factory twice under concurrency; create outside it so the losing key is disposed.
        SigningKey created = Create(keyId, DefaultAlgorithm);
        SigningKey stored = _keys.GetOrAdd(keyId, created);
        if (!ReferenceEquals(stored, created))
        {
            created.Dispose();
        }

        return new(stored);
    }

    /// <summary>Disposes every key.</summary>
    public void Dispose()
    {
        foreach (SigningKey key in _keys.Values)
        {
            key.Dispose();
        }
    }

    private static SigningKey Create(string keyId, SignatureAlgorithm algorithm) => algorithm switch
    {
        SignatureAlgorithm.ES256 => SigningKey.FromECDsa(keyId, ECDsa.Create(ECCurve.NamedCurves.nistP256)),
        SignatureAlgorithm.ES384 => SigningKey.FromECDsa(keyId, ECDsa.Create(ECCurve.NamedCurves.nistP384)),
        SignatureAlgorithm.ES512 => SigningKey.FromECDsa(keyId, ECDsa.Create(ECCurve.NamedCurves.nistP521)),
        _ => SigningKey.FromRsa(keyId, RSA.Create(2048), algorithm),
    };
}

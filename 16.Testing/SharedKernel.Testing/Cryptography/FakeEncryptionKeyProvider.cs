using System.Collections.Concurrent;
using System.Security.Cryptography;
using SharedKernel.Cryptography.Symmetric;

namespace SharedKernel.Testing.Cryptography;

/// <summary>
/// In-memory test double for <see cref="IEncryptionKeyProvider"/> — supports multiple key versions,
/// one designated as current.
/// </summary>
/// <remarks>
/// <para>
/// Promoted from <c>SharedKernel.Cryptography.Tests</c>' internal <c>InMemoryEncryptionKeyProvider</c>
/// test double into this package's public, shared surface (zero behavioral drift; hardened here for
/// thread safety since fakes in this package may be shared across parallel xUnit collections).
/// Multi-key-version support lets a test exercise a key-rotation "decrypt an older payload" scenario
/// exactly like a real Key Vault-backed provider would.
/// </para>
/// <para>
/// <b>TEST-ONLY — NEVER PRODUCTION-SAFE.</b> Key material lives only in an in-process
/// <see cref="ConcurrentDictionary{TKey,TValue}"/> with zero persistence, rotation policy, or
/// Key Vault/HSM-backed hardening — wiring this into a production DI container would silently
/// discard every key on process restart.
/// </para>
/// </remarks>
public sealed class FakeEncryptionKeyProvider : IEncryptionKeyProvider
{
    private readonly ConcurrentDictionary<string, CryptographicKey> _keys = new();
    private readonly Lock _gate = new();
    private string _currentKeyId;

    /// <summary>
    /// Initialises a new <see cref="FakeEncryptionKeyProvider"/>, seeding one key immediately.
    /// </summary>
    /// <param name="currentKeyId">The identifier of the initially-seeded, current key. Defaults to <c>"v1"</c>.</param>
    public FakeEncryptionKeyProvider(string currentKeyId = "v1")
    {
        _currentKeyId = currentKeyId;
        AddKey(currentKeyId);
    }

    /// <summary>Generates and registers a fresh 32-byte key under <paramref name="keyId"/>.</summary>
    /// <param name="keyId">The key version identifier.</param>
    /// <returns>The newly generated <see cref="CryptographicKey"/>.</returns>
    public CryptographicKey AddKey(string keyId)
    {
        byte[] material = new byte[32];
        RandomNumberGenerator.Fill(material);
        var key = new CryptographicKey(keyId, material);
        _keys[keyId] = key;
        return key;
    }

    /// <summary>Designates <paramref name="keyId"/> as the key returned by <see cref="GetCurrentKey"/>.</summary>
    /// <param name="keyId">The key version identifier to make current.</param>
    public void SetCurrentKey(string keyId)
    {
        lock (_gate)
        {
            _currentKeyId = keyId;
        }
    }

    /// <summary>Removes the key registered under <paramref name="keyId"/>, simulating key retirement.</summary>
    /// <param name="keyId">The key version identifier to remove.</param>
    public void RemoveKey(string keyId) => _keys.TryRemove(keyId, out _);

    /// <inheritdoc />
    public CryptographicKey GetCurrentKey()
    {
        string currentKeyId;
        lock (_gate)
        {
            currentKeyId = _currentKeyId;
        }

        return _keys[currentKeyId];
    }

    /// <inheritdoc />
    public CryptographicKey? GetKey(string keyId) => _keys.GetValueOrDefault(keyId);
}

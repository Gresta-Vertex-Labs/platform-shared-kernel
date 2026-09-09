using SharedKernel.Cryptography.Symmetric;

namespace SharedKernel.Cryptography.Tests.Symmetric;

/// <summary>
/// Minimal in-memory <see cref="IEncryptionKeyProvider"/> test double — multiple key versions,
/// one designated as current. Both interface members complete synchronously (an already-completed
/// <see cref="ValueTask{TResult}"/>) — exactly the shape a configuration-based provider takes in
/// production, and the shape that makes <c>ISymmetricEncryptionService</c>'s sync-to-async bridge
/// genuinely non-blocking. Implements <see cref="ISynchronousEncryptionKeyProvider"/> (P-492/
/// WO-081) — this double genuinely never performs blocking I/O, so it honestly earns the marker.
/// </summary>
internal sealed class InMemoryEncryptionKeyProvider : ISynchronousEncryptionKeyProvider
{
    private readonly Dictionary<string, CryptographicKey> _keys = [];
    private string _currentKeyId;

    public InMemoryEncryptionKeyProvider(string currentKeyId = "v1")
    {
        _currentKeyId = currentKeyId;
        AddKey(currentKeyId);
    }

    public CryptographicKey AddKey(string keyId)
    {
        byte[] material = new byte[32];
        System.Security.Cryptography.RandomNumberGenerator.Fill(material);
        return AddKey(keyId, material);
    }

    /// <summary>
    /// Adds a key with caller-supplied (possibly deliberately wrong-sized) material — backs
    /// P-513/WO-083's <c>AesGcmEncryptionService.EnsureKeySize</c> tests, which need a key whose
    /// length is NOT the required 32 bytes.
    /// </summary>
    public CryptographicKey AddKey(string keyId, byte[] material)
    {
        var key = new CryptographicKey(keyId, material);
        _keys[keyId] = key;
        return key;
    }

    public void SetCurrentKey(string keyId) => _currentKeyId = keyId;

    /// <summary>Adds <paramref name="key"/> and immediately designates it current.</summary>
    public void SetCurrentKey(CryptographicKey key)
    {
        _keys[key.Id] = key;
        _currentKeyId = key.Id;
    }

    public void RemoveKey(string keyId) => _keys.Remove(keyId);

    public ValueTask<CryptographicKey> GetCurrentKeyAsync(CancellationToken ct = default) =>
        new(_keys[_currentKeyId]);

    public ValueTask<CryptographicKey?> GetKeyAsync(string keyId, CancellationToken ct = default) =>
        new(_keys.GetValueOrDefault(keyId));
}

using SharedKernel.Cryptography.Symmetric;

namespace SharedKernel.Cryptography.Tests.Symmetric;

/// <summary>
/// Minimal in-memory <see cref="IEncryptionKeyProvider"/> test double — multiple key versions,
/// one designated as current.
/// </summary>
internal sealed class InMemoryEncryptionKeyProvider : IEncryptionKeyProvider
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
        var key = new CryptographicKey(keyId, material);
        _keys[keyId] = key;
        return key;
    }

    public void SetCurrentKey(string keyId) => _currentKeyId = keyId;

    public void RemoveKey(string keyId) => _keys.Remove(keyId);

    public CryptographicKey GetCurrentKey() => _keys[_currentKeyId];

    public CryptographicKey? GetKey(string keyId) => _keys.GetValueOrDefault(keyId);
}

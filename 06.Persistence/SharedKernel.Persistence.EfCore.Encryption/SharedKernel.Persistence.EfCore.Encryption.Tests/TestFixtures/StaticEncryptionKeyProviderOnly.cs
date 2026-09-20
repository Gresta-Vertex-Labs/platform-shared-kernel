using SharedKernel.Cryptography.Symmetric;

namespace SharedKernel.Persistence.EfCore.Encryption.Tests.TestFixtures;

/// <summary>A key provider serving only the ONE named key from <see cref="EncryptionTestHost"/>'s fixed key material, to simulate a key ring that never learned about the "other" key id.</summary>
internal sealed class StaticEncryptionKeyProviderOnly : IEncryptionKeyProvider, ISynchronousEncryptionKeyProvider
{
    private readonly CryptographicKey _key;

    public StaticEncryptionKeyProviderOnly(string keyId)
    {
        var material = keyId switch
        {
            "v1" => EncryptionTestHost.KeyV1,
            "v2" => EncryptionTestHost.KeyV2,
            _ => throw new ArgumentOutOfRangeException(nameof(keyId)),
        };
        _key = new CryptographicKey(keyId, material);
    }

    public CryptographicKey GetCurrentKey() => _key;

    public CryptographicKey? GetKey(string keyId) => keyId == _key.Id ? _key : null;

    public ValueTask<CryptographicKey> GetCurrentKeyAsync(CancellationToken cancellationToken = default) => new(_key);

    public ValueTask<CryptographicKey?> GetKeyAsync(string keyId, CancellationToken cancellationToken = default) => new(GetKey(keyId));
}

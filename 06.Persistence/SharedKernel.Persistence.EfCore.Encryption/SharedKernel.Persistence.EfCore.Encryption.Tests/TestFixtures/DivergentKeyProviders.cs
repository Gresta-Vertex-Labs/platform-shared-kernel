using SharedKernel.Cryptography.Symmetric;

namespace SharedKernel.Persistence.EfCore.Encryption.Tests.TestFixtures;

// Used only by the H10 regression coverage (EncryptionRotationIntegrationTests): two INDEPENDENT key-provider
// instances, one asynchronous and one synchronous, that can be told to report DIFFERENT keys as "current" — the
// exact shape of a real KMS-backed IEncryptionKeyProvider diverging from a periodically-refreshed
// EncryptionKeyRingCache bridge in the window right after an operator flips the current key. Every other fixture
// in this test project (StaticEncryptionKeyProvider, StaticEncryptionKeyProviderOnly) implements BOTH interfaces
// off the SAME backing state, so divergence between them is structurally impossible — these two exist specifically
// to make it possible, deliberately, for one test.

/// <summary>An asynchronous-only key provider whose "current" key is fixed at construction — never refreshed.</summary>
internal sealed class FixedCurrentAsyncKeyProvider(string currentKeyId) : IEncryptionKeyProvider
{
    private static readonly IReadOnlyDictionary<string, CryptographicKey> Keys = new Dictionary<string, CryptographicKey>(StringComparer.Ordinal)
    {
        ["v1"] = new CryptographicKey("v1", EncryptionTestHost.KeyV1),
        ["v2"] = new CryptographicKey("v2", EncryptionTestHost.KeyV2),
    };

    public ValueTask<CryptographicKey> GetCurrentKeyAsync(CancellationToken cancellationToken = default) => new(Keys[currentKeyId]);

    public ValueTask<CryptographicKey?> GetKeyAsync(string keyId, CancellationToken cancellationToken = default) =>
        new(Keys.GetValueOrDefault(keyId));
}

/// <summary>A synchronous-only key provider whose "current" key is fixed at construction — never refreshed.</summary>
internal sealed class FixedCurrentSyncKeyProvider(string currentKeyId) : ISynchronousEncryptionKeyProvider
{
    private static readonly IReadOnlyDictionary<string, CryptographicKey> Keys = new Dictionary<string, CryptographicKey>(StringComparer.Ordinal)
    {
        ["v1"] = new CryptographicKey("v1", EncryptionTestHost.KeyV1),
        ["v2"] = new CryptographicKey("v2", EncryptionTestHost.KeyV2),
    };

    public CryptographicKey GetCurrentKey() => Keys[currentKeyId];

    public CryptographicKey? GetKey(string keyId) => Keys.GetValueOrDefault(keyId);
}

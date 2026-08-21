using System.Security.Cryptography;
using SharedKernel.Cryptography.Symmetric;

namespace SharedKernel.Integration.Webhooks.Tests.TestSupport;

/// <summary>
/// Minimal <see cref="IEncryptionKeyProvider"/> test double backed by a single freshly generated
/// AES-256 key — no real key vault/secret store involved.
/// </summary>
internal sealed class InMemoryEncryptionKeyProvider : IEncryptionKeyProvider
{
    private readonly CryptographicKey _key = new("test-key-v1", RandomNumberGenerator.GetBytes(32));

    public CryptographicKey GetCurrentKey() => _key;

    public CryptographicKey? GetKey(string keyId) => keyId == _key.Id ? _key : null;
}

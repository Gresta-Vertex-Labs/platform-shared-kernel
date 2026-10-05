using System.Security.Cryptography;
using SharedKernel.Cryptography.Symmetric;

namespace SharedKernel.Cryptography.Tests.TestDoubles;

internal static class TestKeys
{
    public static CryptographicKey Create(string id, int length = 32) => new(id, RandomNumberGenerator.GetBytes(length));

    public static StaticEncryptionKeyProvider Provider(string currentKeyId, params CryptographicKey[] keys) => new(currentKeyId, keys);

    public static StaticEncryptionKeyProvider SingleKeyProvider(string id = "key-1", int length = 32) =>
        new(id, [Create(id, length)]);
}

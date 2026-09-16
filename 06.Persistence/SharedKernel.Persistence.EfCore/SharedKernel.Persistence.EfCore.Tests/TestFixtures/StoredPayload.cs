using FluentAssertions;
using SharedKernel.Cryptography.Symmetric;

namespace SharedKernel.Persistence.EfCore.Tests.TestFixtures;

/// <summary>Reads the canonical <see cref="EncryptedPayload"/> encoding an encrypted column stores.</summary>
internal static class StoredPayload
{
    /// <summary>Parses <paramref name="stored"/>, asserting it is a well-formed payload.</summary>
    public static EncryptedPayload Parse(string stored)
    {
        EncryptedPayload.TryParse(stored, out var payload).Should().BeTrue(
            "an encrypted column stores the canonical EncryptedPayload.ToString() encoding");
        return payload!;
    }

    /// <summary>Returns the key id recorded in <paramref name="stored"/>.</summary>
    public static string KeyIdOf(string stored) => Parse(stored).KeyId;

    /// <summary>A well-formed payload recording <paramref name="keyId"/> that no key can decrypt.</summary>
    public static string ForKeyId(string keyId) =>
        new EncryptedPayload(keyId, new byte[EncryptedPayload.NonceSize], new byte[8], new byte[EncryptedPayload.TagSize]).ToString();
}

using System.Security.Cryptography;

namespace SharedKernel.Cryptography.Signing;

/// <summary>
/// Signs and verifies data using HMACSHA256. <see cref="Verify"/> uses
/// <see cref="CryptographicOperations.FixedTimeEquals(System.ReadOnlySpan{byte}, System.ReadOnlySpan{byte})"/>
/// for a timing-attack-resistant comparison — never <c>==</c> or <c>SequenceEqual</c>.
/// </summary>
public sealed class HmacSha256Signer : IHmacSigner
{
    /// <inheritdoc />
    public byte[] Sign(byte[] data, byte[] secret)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(secret);

        return HMACSHA256.HashData(secret, data);
    }

    /// <inheritdoc />
    public bool Verify(byte[] data, byte[] signature, byte[] secret)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(signature);
        ArgumentNullException.ThrowIfNull(secret);

        byte[] expected = HMACSHA256.HashData(secret, data);
        return CryptographicOperations.FixedTimeEquals(expected, signature);
    }
}

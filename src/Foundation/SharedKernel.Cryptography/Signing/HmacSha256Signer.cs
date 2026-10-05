using System.Security.Cryptography;

namespace SharedKernel.Cryptography.Signing;

/// <summary><see cref="IHmacSigner"/> backed by <see cref="HMACSHA256"/>.</summary>
/// <remarks>Stateless and thread-safe.</remarks>
public sealed class HmacSha256Signer : IHmacSigner
{
    /// <summary>The shortest key accepted: 32 bytes, the SHA-256 output size.</summary>
    public const int MinimumKeyLength = 32;

    /// <inheritdoc />
    public byte[] Sign(ReadOnlySpan<byte> data, ReadOnlySpan<byte> key)
    {
        EnsureKeyLength(key);
        return HMACSHA256.HashData(key, data);
    }

    /// <inheritdoc />
    public bool Verify(ReadOnlySpan<byte> data, ReadOnlySpan<byte> signature, ReadOnlySpan<byte> key)
    {
        EnsureKeyLength(key);

        Span<byte> expected = stackalloc byte[HMACSHA256.HashSizeInBytes];
        HMACSHA256.HashData(key, data, expected);
        return CryptographicOperations.FixedTimeEquals(expected, signature);
    }

    private static void EnsureKeyLength(ReadOnlySpan<byte> key)
    {
        if (key.Length < MinimumKeyLength)
        {
            throw new ArgumentException($"The HMAC key must be at least {MinimumKeyLength} bytes.", nameof(key));
        }
    }
}

using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace SharedKernel.Cryptography.Totp;

/// <summary>
/// RFC 4226 HOTP (HMAC-based One-Time Password) implementation — HMAC-SHA1 by default (per the
/// RFC), with HMAC-SHA256/HMAC-SHA512 available as an opt-in extension. Pure BCL
/// <see cref="System.Security.Cryptography"/> only.
/// </summary>
public sealed class HotpGenerator : IHotpGenerator
{
    private const int MinDigits = 1;
    private const int MaxDigits = 9; // 10^9 fits safely in a long-backed modulo; no RFC/TOTP vector ever exceeds 8.

    /// <inheritdoc />
    public string GenerateCode(byte[] secret, long counter, int digits = 6, HotpAlgorithm algorithm = HotpAlgorithm.Sha1)
    {
        ArgumentNullException.ThrowIfNull(secret);
        ValidateDigits(digits);

        Span<byte> counterBytes = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(counterBytes, counter);

        byte[] hash = ComputeHmac(secret, counterBytes, algorithm);

        // RFC 4226 §5.3 dynamic truncation.
        int offset = hash[^1] & 0x0F;
        int binary =
            ((hash[offset] & 0x7F) << 24)
            | ((hash[offset + 1] & 0xFF) << 16)
            | ((hash[offset + 2] & 0xFF) << 8)
            | (hash[offset + 3] & 0xFF);

        long divisor = Pow10(digits);
        long code = binary % divisor;

        return code.ToString(CultureInfo.InvariantCulture).PadLeft(digits, '0');
    }

    /// <inheritdoc />
    public bool ValidateCode(byte[] secret, string code, long counter, int digits = 6, HotpAlgorithm algorithm = HotpAlgorithm.Sha1)
    {
        ArgumentNullException.ThrowIfNull(secret);
        ArgumentNullException.ThrowIfNull(code);

        string expected = GenerateCode(secret, counter, digits, algorithm);

        byte[] expectedBytes = Encoding.ASCII.GetBytes(expected);
        byte[] actualBytes = Encoding.ASCII.GetBytes(code);

        // A one-time-password code is a secret-derived value exactly like an HMAC/signature —
        // constant-time comparison is required (root CLAUDE.md rule). FixedTimeEquals requires
        // equal-length spans; a length mismatch is itself not a timing-sensitive fact (the digit
        // count is a public parameter), so short-circuiting on it first is safe.
        return expectedBytes.Length == actualBytes.Length
            && CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
    }

    private static byte[] ComputeHmac(byte[] secret, ReadOnlySpan<byte> counterBytes, HotpAlgorithm algorithm) =>
        algorithm switch
        {
            HotpAlgorithm.Sha1 => HMACSHA1.HashData(secret, counterBytes),
            HotpAlgorithm.Sha256 => HMACSHA256.HashData(secret, counterBytes),
            HotpAlgorithm.Sha512 => HMACSHA512.HashData(secret, counterBytes),
            _ => throw new ArgumentOutOfRangeException(nameof(algorithm), algorithm, "Unsupported HOTP algorithm."),
        };

    private static void ValidateDigits(int digits)
    {
        if (digits is < MinDigits or > MaxDigits)
        {
            throw new ArgumentOutOfRangeException(
                nameof(digits),
                digits,
                $"digits must be between {MinDigits} and {MaxDigits}.");
        }
    }

    private static long Pow10(int exponent)
    {
        long result = 1;
        for (int i = 0; i < exponent; i++)
        {
            result *= 10;
        }

        return result;
    }
}

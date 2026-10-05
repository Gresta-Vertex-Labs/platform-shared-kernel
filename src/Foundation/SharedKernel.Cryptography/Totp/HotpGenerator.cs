using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace SharedKernel.Cryptography.Totp;

/// <summary>RFC 4226 HOTP.</summary>
/// <remarks>Stateless and thread-safe.</remarks>
public sealed class HotpGenerator : IHotpGenerator
{
    /// <summary>The fewest digits accepted, as RFC 4226 requires.</summary>
    public const int MinimumDigits = 6;

    /// <summary>The most digits accepted.</summary>
    public const int MaximumDigits = 8;

    /// <summary>The shortest secret accepted: 16 bytes (128 bits), as RFC 4226 requires. 20 bytes are recommended.</summary>
    public const int MinimumSecretLength = 16;

    /// <inheritdoc />
    public string GenerateCode(ReadOnlySpan<byte> secret, long counter, int digits = 6, HotpAlgorithm algorithm = HotpAlgorithm.Sha1)
    {
        Validate(secret, counter, digits, algorithm);
        return Compute(secret, counter, digits, algorithm).ToString(CultureInfo.InvariantCulture).PadLeft(digits, '0');
    }

    /// <inheritdoc />
    public bool ValidateCode(ReadOnlySpan<byte> secret, string code, long counter, int digits = 6, HotpAlgorithm algorithm = HotpAlgorithm.Sha1)
    {
        ArgumentNullException.ThrowIfNull(code);
        Validate(secret, counter, digits, algorithm);

        return OtpCode.TryNormalize(code, digits, out string? normalized)
            && Matches(secret, normalized, counter, digits, algorithm);
    }

    internal static bool Matches(ReadOnlySpan<byte> secret, string normalizedCode, long counter, int digits, HotpAlgorithm algorithm)
    {
        string expected = Compute(secret, counter, digits, algorithm).ToString(CultureInfo.InvariantCulture).PadLeft(digits, '0');
        return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(normalizedCode));
    }

    internal static void Validate(ReadOnlySpan<byte> secret, long counter, int digits, HotpAlgorithm algorithm)
    {
        if (secret.Length < MinimumSecretLength)
        {
            throw new ArgumentException($"The secret must be at least {MinimumSecretLength} bytes.", nameof(secret));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(counter);
        ArgumentOutOfRangeException.ThrowIfLessThan(digits, MinimumDigits);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(digits, MaximumDigits);

        if (!Enum.IsDefined(algorithm))
        {
            throw new ArgumentOutOfRangeException(nameof(algorithm), algorithm, "Unknown HOTP algorithm.");
        }
    }

    private static int Compute(ReadOnlySpan<byte> secret, long counter, int digits, HotpAlgorithm algorithm)
    {
        Span<byte> counterBytes = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(counterBytes, counter);

        Span<byte> hash = stackalloc byte[64];
        int length = algorithm switch
        {
            HotpAlgorithm.Sha1 => HMACSHA1.HashData(secret, counterBytes, hash),
            HotpAlgorithm.Sha256 => HMACSHA256.HashData(secret, counterBytes, hash),
            _ => HMACSHA512.HashData(secret, counterBytes, hash),
        };

        // RFC 4226 section 5.3: dynamic truncation.
        int offset = hash[length - 1] & 0x0F;
        int binary = ((hash[offset] & 0x7F) << 24)
            | (hash[offset + 1] << 16)
            | (hash[offset + 2] << 8)
            | hash[offset + 3];

        CryptographicOperations.ZeroMemory(hash);
        return binary % Pow10(digits);
    }

    private static int Pow10(int digits) => digits switch
    {
        6 => 1_000_000,
        7 => 10_000_000,
        _ => 100_000_000,
    };
}

/// <summary>Normalizes codes typed by users.</summary>
internal static class OtpCode
{
    public static bool TryNormalize(string code, int digits, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? normalized)
    {
        normalized = null;
        if (code.Length > 32)
        {
            return false;
        }

        Span<char> buffer = stackalloc char[code.Length];
        int count = 0;
        foreach (char c in code)
        {
            if (c is ' ' or '-')
            {
                continue;
            }

            if (!char.IsAsciiDigit(c))
            {
                return false;
            }

            buffer[count++] = c;
        }

        if (count != digits)
        {
            return false;
        }

        normalized = new string(buffer[..count]);
        return true;
    }
}

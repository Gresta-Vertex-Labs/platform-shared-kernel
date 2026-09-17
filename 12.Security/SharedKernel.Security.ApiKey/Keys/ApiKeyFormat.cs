using System.Buffers.Text;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;

namespace SharedKernel.Security.ApiKey.Keys;

// Managed key layout: {prefix}_{keyId}_{secret}{checksum}
//   prefix    2-32 chars, lowercase letters, digits and single underscores, starting with a letter
//   keyId     16 Base62 chars (~95 bits), the non-secret lookup id
//   secret    32 Base62 chars (~190 bits)
//   checksum  6 Base62 chars, CRC-32 of everything before it, so scanners and the validator reject typos and
//             random strings without a store lookup
internal static class ApiKeyFormat
{
    internal const string Alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";
    internal const int KeyIdLength = 16;
    internal const int SecretLength = 32;
    internal const int ChecksumLength = 6;

    private const int MinPrefixLength = 2;
    private const int MaxPrefixLength = 32;
    private const int MaxKeyLength = MaxPrefixLength + 1 + KeyIdLength + 1 + SecretLength + ChecksumLength;

    private static readonly uint[] CrcTable = BuildCrcTable();

    public static bool IsValidPrefix([NotNullWhen(true)] string? prefix)
    {
        if (prefix is null || prefix.Length is < MinPrefixLength or > MaxPrefixLength || !char.IsAsciiLetterLower(prefix[0]))
        {
            return false;
        }

        for (int i = 1; i < prefix.Length; i++)
        {
            char c = prefix[i];
            bool valid = char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c)
                || (c == '_' && prefix[i - 1] != '_' && i != prefix.Length - 1);
            if (!valid)
            {
                return false;
            }
        }

        return true;
    }

    public static string Compose(string prefix, string keyId, string secret)
    {
        string body = $"{prefix}_{keyId}_{secret}";
        return body + Checksum(body);
    }

    public static bool TryParse(string key, [NotNullWhen(true)] out string? prefix, [NotNullWhen(true)] out string? keyId)
    {
        prefix = null;
        keyId = null;

        if (key.Length > MaxKeyLength || key.Length < MinPrefixLength + KeyIdLength + SecretLength + ChecksumLength + 2)
        {
            return false;
        }

        int tailStart = key.Length - SecretLength - ChecksumLength;
        int keyIdStart = tailStart - 1 - KeyIdLength;
        if (key[tailStart - 1] != '_' || key[keyIdStart - 1] != '_')
        {
            return false;
        }

        ReadOnlySpan<char> id = key.AsSpan(keyIdStart, KeyIdLength);
        ReadOnlySpan<char> tail = key.AsSpan(tailStart);
        if (id.ContainsAnyExcept(Alphabet) || tail.ContainsAnyExcept(Alphabet))
        {
            return false;
        }

        string candidatePrefix = key[..(keyIdStart - 1)];
        if (!IsValidPrefix(candidatePrefix))
        {
            return false;
        }

        string body = key[..^ChecksumLength];
        if (!string.Equals(Checksum(body), key[^ChecksumLength..], StringComparison.Ordinal))
        {
            return false;
        }

        prefix = candidatePrefix;
        keyId = id.ToString();
        return true;
    }

    public static string Hash(string key) => Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(key)));

    private static string Checksum(string body)
    {
        uint crc = 0xFFFFFFFF;
        foreach (char c in body)
        {
            crc = CrcTable[(crc ^ (byte)c) & 0xFF] ^ (crc >> 8);
        }

        crc ^= 0xFFFFFFFF;

        Span<char> digits = stackalloc char[ChecksumLength];
        for (int i = ChecksumLength - 1; i >= 0; i--)
        {
            digits[i] = Alphabet[(int)(crc % 62)];
            crc /= 62;
        }

        return new string(digits);
    }

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            }

            table[n] = c;
        }

        return table;
    }
}

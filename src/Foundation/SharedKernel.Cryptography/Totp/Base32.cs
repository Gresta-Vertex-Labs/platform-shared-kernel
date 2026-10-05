using System.Text;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Cryptography.Totp;

/// <summary>RFC 4648 Base32, the encoding authenticator apps use for shared secrets.</summary>
public static class Base32
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    /// <summary>Encodes bytes as uppercase Base32 without padding.</summary>
    /// <param name="data">The bytes to encode.</param>
    /// <returns>The encoded text.</returns>
    public static string Encode(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
        {
            return string.Empty;
        }

        var builder = new StringBuilder(((data.Length * 8) + 4) / 5);
        int buffer = 0;
        int bits = 0;

        foreach (byte b in data)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                bits -= 5;
                builder.Append(Alphabet[(buffer >> bits) & 0x1F]);
            }
        }

        if (bits > 0)
        {
            builder.Append(Alphabet[(buffer << (5 - bits)) & 0x1F]);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Decodes Base32 text. Letters are case-insensitive, trailing <c>=</c> padding is allowed, and the unused bits of
    /// the last character must be zero.
    /// </summary>
    /// <param name="text">The text to decode.</param>
    /// <returns>The bytes, or <see cref="CryptographyErrorCodes.InvalidBase32Encoding"/> for malformed text.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    public static Result<byte[]> Decode(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        ReadOnlySpan<char> value = text.AsSpan().TrimEnd('=');
        if (value.Length % 8 is 1 or 3 or 6)
        {
            return Invalid();
        }

        byte[] output = new byte[value.Length * 5 / 8];
        int buffer = 0;
        int bits = 0;
        int written = 0;

        foreach (char c in value)
        {
            // ASCII only: char.ToUpperInvariant maps some non-ASCII letters, such as U+017F, onto the alphabet.
            int index = char.IsAscii(c) ? Alphabet.IndexOf(char.ToUpperInvariant(c), StringComparison.Ordinal) : -1;
            if (index < 0)
            {
                return Invalid();
            }

            buffer = ((buffer << 5) | index) & 0xFFF;
            bits += 5;
            if (bits >= 8)
            {
                bits -= 8;
                output[written++] = (byte)(buffer >> bits);
            }
        }

        if ((buffer & ((1 << bits) - 1)) != 0)
        {
            return Invalid();
        }

        return output;
    }

    private static Error Invalid() =>
        Error.Validation(CryptographyErrorCodes.InvalidBase32Encoding, "The text is not valid Base32.");
}

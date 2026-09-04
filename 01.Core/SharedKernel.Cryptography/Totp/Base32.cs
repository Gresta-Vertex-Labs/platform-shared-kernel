using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Cryptography.Totp;

/// <summary>
/// Encodes and decodes RFC 4648 Base32 — unpadded, the encoding authenticator apps (Google
/// Authenticator, Microsoft Authenticator, etc.) expect for TOTP/HOTP shared secrets.
/// </summary>
/// <remarks>
/// This is a plain byte-to-text codec, not a cryptographic primitive in its own right — it exists
/// here (rather than in <c>SharedKernel.Core</c>'s BCL extension methods) because its only
/// consumer on this platform is the TOTP/HOTP surface in this package (secret encoding for
/// provisioning URIs, and recovery-code rendering via <see cref="RecoveryCodeGenerator"/>).
/// </remarks>
public static class Base32
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    /// <summary>
    /// Encodes <paramref name="data"/> as unpadded RFC 4648 Base32 text (uppercase).
    /// </summary>
    /// <param name="data">The bytes to encode. May be empty.</param>
    /// <returns>The Base32-encoded text, with no <c>=</c> padding characters.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="data"/> is <see langword="null"/>.</exception>
    public static string Encode(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);

        if (data.Length == 0)
        {
            return string.Empty;
        }

        var builder = new System.Text.StringBuilder((data.Length * 8 / 5) + 1);

        int bitBuffer = 0;
        int bitsInBuffer = 0;

        foreach (byte b in data)
        {
            bitBuffer = (bitBuffer << 8) | b;
            bitsInBuffer += 8;

            while (bitsInBuffer >= 5)
            {
                bitsInBuffer -= 5;
                int index = (bitBuffer >> bitsInBuffer) & 0b11111;
                builder.Append(Alphabet[index]);
            }
        }

        if (bitsInBuffer > 0)
        {
            int index = (bitBuffer << (5 - bitsInBuffer)) & 0b11111;
            builder.Append(Alphabet[index]);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Decodes unpadded RFC 4648 Base32 text back to bytes. Case-insensitive (both upper- and
    /// lower-case letters are accepted) since users frequently paste secrets in mixed case.
    /// </summary>
    /// <param name="base32Text">The Base32 text to decode.</param>
    /// <returns>
    /// A successful <see cref="Result{T}"/> carrying the decoded bytes, or a failure
    /// <see cref="Result{T}"/> when <paramref name="base32Text"/> contains a character outside the
    /// RFC 4648 Base32 alphabet (including <c>=</c> padding, which this codec never expects), or
    /// has a length that cannot correspond to any unpadded Base32 encoding.
    /// </returns>
    /// <remarks>
    /// NEVER THROWS ON MALFORMED INPUT — mirrors <see cref="Symmetric.ISymmetricEncryptionService.Decrypt"/>'s
    /// established shape: an expected/anticipated failure (attacker- or user-supplied garbage)
    /// surfaces as a <see cref="Result{T}"/> failure, never an exception. Only a genuinely
    /// programmer-error input (<see langword="null"/>) throws.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="base32Text"/> is <see langword="null"/>.</exception>
    public static Result<byte[]> Decode(string base32Text)
    {
        ArgumentNullException.ThrowIfNull(base32Text);

        if (base32Text.Length == 0)
        {
            return Array.Empty<byte>();
        }

        // Valid unpadded Base32 text lengths (mod 8) are 0, 2, 4, 5, 7 — these correspond to a
        // whole number of encoded bytes (0, 1, 2, 3, 4 leftover bytes per 5-byte group). A
        // remainder of 1, 3, or 6 cannot correspond to any byte sequence and is malformed.
        int remainder = base32Text.Length % 8;
        if (remainder is 1 or 3 or 6)
        {
            return Error.Validation(
                CryptographyErrorCodes.InvalidBase32Encoding,
                "The supplied text's length does not correspond to any valid unpadded Base32 encoding.");
        }

        var output = new List<byte>((base32Text.Length * 5) / 8);
        int bitBuffer = 0;
        int bitsInBuffer = 0;

        foreach (char c in base32Text)
        {
            int value = Alphabet.IndexOf(char.ToUpperInvariant(c));
            if (value < 0)
            {
                return Error.Validation(
                    CryptographyErrorCodes.InvalidBase32Encoding,
                    $"The character '{c}' is not part of the RFC 4648 Base32 alphabet.");
            }

            bitBuffer = (bitBuffer << 5) | value;
            bitsInBuffer += 5;

            if (bitsInBuffer >= 8)
            {
                bitsInBuffer -= 8;
                output.Add((byte)((bitBuffer >> bitsInBuffer) & 0xFF));
            }
        }

        return output.ToArray();
    }
}

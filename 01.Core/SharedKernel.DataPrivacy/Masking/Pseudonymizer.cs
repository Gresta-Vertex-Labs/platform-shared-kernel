using System.Buffers;
using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace SharedKernel.DataPrivacy.Masking;

/// <summary>
/// Replaces a value with a stable token derived from it with HMAC-SHA256 and a secret key: the same
/// value always gives the same token, so log lines and analytics about one person can still be
/// correlated, but the token cannot be turned back into the value without the key.
/// </summary>
/// <remarks>
/// <para>
/// Pseudonymized data is still personal data under GDPR (Recital 26) and KVKK, because whoever holds
/// the key can link it back. Keep the key in a secret store, separate from the data. Rotating the
/// key changes every token, which also breaks correlation with older logs.
/// </para>
/// <para>
/// The input is hashed exactly as given. Normalize it first when different spellings mean the
/// same person, for example by lowercasing an email address.
/// </para>
/// </remarks>
public sealed class Pseudonymizer
{
    /// <summary>The minimum key length, in bytes.</summary>
    public const int MinimumKeyLength = 32;

    /// <summary>The length of every token <see cref="Pseudonymize(string?)"/> returns for non-empty input.</summary>
    public const int TokenLength = 22;

    private const int TokenBytes = 16;
    private const int StackLimit = 256;

    private readonly byte[] _key;

    /// <summary>Initializes a new instance of the <see cref="Pseudonymizer"/> class.</summary>
    /// <param name="key">The secret key; at least <see cref="MinimumKeyLength"/> bytes. It is copied.</param>
    /// <exception cref="ArgumentException"><paramref name="key"/> is shorter than <see cref="MinimumKeyLength"/>.</exception>
    public Pseudonymizer(ReadOnlySpan<byte> key)
    {
        if (key.Length < MinimumKeyLength)
        {
            throw new ArgumentException($"The pseudonymization key must be at least {MinimumKeyLength} bytes.", nameof(key));
        }

        _key = key.ToArray();
    }

    /// <summary>Returns the token for <paramref name="value"/>.</summary>
    /// <param name="value">The value.</param>
    /// <returns>
    /// A <see cref="TokenLength"/>-character base64url token (the first 128 bits of the HMAC), or
    /// <see cref="string.Empty"/> for <see langword="null"/> or empty input.
    /// </returns>
    public string Pseudonymize(string? value) => string.IsNullOrEmpty(value) ? string.Empty : Pseudonymize(value.AsSpan());

    /// <summary>Returns the token for <paramref name="value"/>.</summary>
    /// <param name="value">The value.</param>
    /// <returns>A <see cref="TokenLength"/>-character base64url token, or <see cref="string.Empty"/> for empty input.</returns>
    public string Pseudonymize(ReadOnlySpan<char> value)
    {
        if (value.IsEmpty)
        {
            return string.Empty;
        }

        int maxBytes = Encoding.UTF8.GetMaxByteCount(value.Length);
        byte[]? rented = maxBytes > StackLimit ? ArrayPool<byte>.Shared.Rent(maxBytes) : null;
        Span<byte> utf8 = rented ?? stackalloc byte[StackLimit];

        try
        {
            int written = Encoding.UTF8.GetBytes(value, utf8);
            Span<byte> hash = stackalloc byte[HMACSHA256.HashSizeInBytes];
            HMACSHA256.HashData(_key, utf8[..written], hash);
            return Base64Url.EncodeToString(hash[..TokenBytes]);
        }
        finally
        {
            if (rented is not null)
            {
                CryptographicOperations.ZeroMemory(rented.AsSpan(0, maxBytes));
                ArrayPool<byte>.Shared.Return(rented);
            }
        }
    }
}

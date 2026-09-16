using System.Buffers.Text;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace SharedKernel.Cryptography.Symmetric;

/// <summary>An AES-256-GCM ciphertext with the key id, nonce and authentication tag needed to decrypt it.</summary>
/// <remarks>
/// <para>
/// <b>Storage format.</b> <see cref="ToBytes"/> and <see cref="ToString"/> write one versioned layout, which
/// <see cref="TryParse(ReadOnlySpan{byte}, out EncryptedPayload)"/> reads back:
/// <c>[version 0x01][key id length: 1 byte][key id: UTF-8][nonce: 12 bytes][tag: 16 bytes][ciphertext]</c>.
/// <see cref="ToString"/> is that layout in unpadded Base64Url, safe for URLs, headers and cookies.
/// </para>
/// <para>
/// The associated data is authenticated but never stored. The caller supplies the same bytes again to decrypt.
/// </para>
/// </remarks>
public sealed class EncryptedPayload
{
    /// <summary>The nonce length: 12 bytes.</summary>
    public const int NonceSize = 12;

    /// <summary>The authentication tag length: 16 bytes.</summary>
    public const int TagSize = 16;

    private const byte FormatVersion = 0x01;
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private readonly byte[] _nonce;
    private readonly byte[] _ciphertext;
    private readonly byte[] _tag;

    /// <summary>Creates a payload from its parts. The buffers are copied.</summary>
    /// <param name="keyId">The id of the key that encrypted the payload.</param>
    /// <param name="nonce">The 12-byte nonce.</param>
    /// <param name="ciphertext">The ciphertext. May be empty.</param>
    /// <param name="tag">The 16-byte authentication tag.</param>
    /// <exception cref="ArgumentException">A part has the wrong size, or <paramref name="keyId"/> is not a valid key id.</exception>
    public EncryptedPayload(string keyId, ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> ciphertext, ReadOnlySpan<byte> tag)
    {
        CryptographicKey.ValidateId(keyId, nameof(keyId));

        if (nonce.Length != NonceSize)
        {
            throw new ArgumentException($"The nonce must be {NonceSize} bytes.", nameof(nonce));
        }

        if (tag.Length != TagSize)
        {
            throw new ArgumentException($"The tag must be {TagSize} bytes.", nameof(tag));
        }

        KeyId = keyId;
        _nonce = nonce.ToArray();
        _ciphertext = ciphertext.ToArray();
        _tag = tag.ToArray();
    }

    /// <summary>The id of the key that encrypted the payload.</summary>
    public string KeyId { get; }

    /// <summary>The 12-byte nonce.</summary>
    public ReadOnlySpan<byte> Nonce => _nonce;

    /// <summary>The ciphertext, the same length as the plaintext.</summary>
    public ReadOnlySpan<byte> Ciphertext => _ciphertext;

    /// <summary>The 16-byte authentication tag.</summary>
    public ReadOnlySpan<byte> Tag => _tag;

    /// <summary>Writes the payload in the storage format.</summary>
    /// <returns>The encoded payload.</returns>
    public byte[] ToBytes()
    {
        int keyIdLength = Encoding.UTF8.GetByteCount(KeyId);
        byte[] buffer = new byte[2 + keyIdLength + NonceSize + TagSize + _ciphertext.Length];
        Span<byte> span = buffer;

        span[0] = FormatVersion;
        span[1] = (byte)keyIdLength;
        int offset = 2 + Encoding.UTF8.GetBytes(KeyId, span[2..]);
        _nonce.CopyTo(span[offset..]);
        offset += NonceSize;
        _tag.CopyTo(span[offset..]);
        offset += TagSize;
        _ciphertext.CopyTo(span[offset..]);

        return buffer;
    }

    /// <summary>Writes the payload in the storage format as unpadded Base64Url.</summary>
    /// <returns>The encoded payload.</returns>
    public override string ToString() => Base64Url.EncodeToString(ToBytes());

    /// <summary>Reads a payload written by <see cref="ToBytes"/>. Never throws for malformed input.</summary>
    /// <param name="data">The encoded payload.</param>
    /// <param name="payload">The payload, when successful.</param>
    /// <returns><see langword="true"/> when <paramref name="data"/> is a well-formed payload.</returns>
    public static bool TryParse(ReadOnlySpan<byte> data, [NotNullWhen(true)] out EncryptedPayload? payload)
    {
        payload = null;
        if (data.Length < 2 + 1 + NonceSize + TagSize || data[0] != FormatVersion)
        {
            return false;
        }

        int keyIdLength = data[1];
        if (keyIdLength == 0 || data.Length < 2 + keyIdLength + NonceSize + TagSize)
        {
            return false;
        }

        string keyId;
        try
        {
            keyId = StrictUtf8.GetString(data.Slice(2, keyIdLength));
        }
        catch (DecoderFallbackException)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(keyId))
        {
            return false;
        }

        int offset = 2 + keyIdLength;
        payload = new EncryptedPayload(
            keyId,
            data.Slice(offset, NonceSize),
            data[(offset + NonceSize + TagSize)..],
            data.Slice(offset + NonceSize, TagSize));
        return true;
    }

    /// <summary>Reads a payload written by <see cref="ToString"/>. Never throws for malformed input.</summary>
    /// <param name="encoded">The Base64Url-encoded payload.</param>
    /// <param name="payload">The payload, when successful.</param>
    /// <returns><see langword="true"/> when <paramref name="encoded"/> is a well-formed payload.</returns>
    public static bool TryParse([NotNullWhen(true)] string? encoded, [NotNullWhen(true)] out EncryptedPayload? payload)
    {
        payload = null;
        if (string.IsNullOrEmpty(encoded))
        {
            return false;
        }

        byte[] buffer = new byte[Base64Url.GetMaxDecodedLength(encoded.Length)];
        try
        {
            return Base64Url.TryDecodeFromChars(encoded, buffer, out int written)
                && TryParse(buffer.AsSpan(0, written), out payload);
        }
        catch (FormatException)
        {
            // Base64Url.TryDecodeFromChars throws, rather than returning false, for characters outside its alphabet.
            return false;
        }
    }
}

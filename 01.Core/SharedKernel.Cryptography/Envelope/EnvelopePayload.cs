using System.Buffers.Binary;
using System.Buffers.Text;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using SharedKernel.Cryptography.Symmetric;

namespace SharedKernel.Cryptography.Envelope;

/// <summary>A self-contained envelope-encrypted value: the wrapped data key and the AES-256-GCM ciphertext it protects.</summary>
/// <remarks>
/// <para>
/// <b>Storage format.</b>
/// <c>[version 0x02][master key id length: 1 byte][master key id: UTF-8][wrapped key length: uint16 BE][wrapped key][nonce: 12 bytes][tag: 16 bytes][ciphertext]</c>.
/// <see cref="ToString"/> is that layout in unpadded Base64Url.
/// </para>
/// <para>
/// The version, master key id and wrapped key are authenticated together with the caller's associated data, so
/// none of them can be swapped between payloads.
/// </para>
/// </remarks>
public sealed class EnvelopePayload
{
    /// <summary>The longest wrapped key accepted, in bytes.</summary>
    public const int MaxWrappedKeyLength = 4096;

    internal const byte FormatVersion = 0x02;
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private readonly byte[] _wrappedKey;
    private readonly byte[] _nonce;
    private readonly byte[] _ciphertext;
    private readonly byte[] _tag;

    /// <summary>Creates a payload from its parts. The buffers are copied.</summary>
    /// <param name="masterKeyId">The id of the master key that wrapped the data key. At most 255 UTF-8 bytes.</param>
    /// <param name="wrappedKey">The wrapped data key. 1 to <see cref="MaxWrappedKeyLength"/> bytes.</param>
    /// <param name="nonce">The 12-byte nonce.</param>
    /// <param name="ciphertext">The ciphertext. May be empty.</param>
    /// <param name="tag">The 16-byte authentication tag.</param>
    /// <exception cref="ArgumentException">A part has the wrong size, or <paramref name="masterKeyId"/> is not valid.</exception>
    public EnvelopePayload(
        string masterKeyId,
        ReadOnlySpan<byte> wrappedKey,
        ReadOnlySpan<byte> nonce,
        ReadOnlySpan<byte> ciphertext,
        ReadOnlySpan<byte> tag)
    {
        CryptographicKey.ValidateId(masterKeyId, nameof(masterKeyId));

        if (wrappedKey.Length is 0 or > MaxWrappedKeyLength)
        {
            throw new ArgumentException($"The wrapped key must be 1 to {MaxWrappedKeyLength} bytes.", nameof(wrappedKey));
        }

        if (nonce.Length != EncryptedPayload.NonceSize)
        {
            throw new ArgumentException($"The nonce must be {EncryptedPayload.NonceSize} bytes.", nameof(nonce));
        }

        if (tag.Length != EncryptedPayload.TagSize)
        {
            throw new ArgumentException($"The tag must be {EncryptedPayload.TagSize} bytes.", nameof(tag));
        }

        MasterKeyId = masterKeyId;
        _wrappedKey = wrappedKey.ToArray();
        _nonce = nonce.ToArray();
        _ciphertext = ciphertext.ToArray();
        _tag = tag.ToArray();
    }

    /// <summary>The id of the master key that wrapped the data key.</summary>
    public string MasterKeyId { get; }

    /// <summary>The wrapped data key.</summary>
    public ReadOnlySpan<byte> WrappedKey => _wrappedKey;

    /// <summary>The 12-byte nonce.</summary>
    public ReadOnlySpan<byte> Nonce => _nonce;

    /// <summary>The ciphertext.</summary>
    public ReadOnlySpan<byte> Ciphertext => _ciphertext;

    /// <summary>The 16-byte authentication tag.</summary>
    public ReadOnlySpan<byte> Tag => _tag;

    /// <summary>Writes the payload in the storage format.</summary>
    /// <returns>The encoded payload.</returns>
    public byte[] ToBytes()
    {
        byte[] header = WriteHeader(MasterKeyId, _wrappedKey);
        byte[] buffer = new byte[header.Length + EncryptedPayload.NonceSize + EncryptedPayload.TagSize + _ciphertext.Length];
        Span<byte> span = buffer;

        header.CopyTo(span);
        int offset = header.Length;
        _nonce.CopyTo(span[offset..]);
        offset += EncryptedPayload.NonceSize;
        _tag.CopyTo(span[offset..]);
        offset += EncryptedPayload.TagSize;
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
    public static bool TryParse(ReadOnlySpan<byte> data, [NotNullWhen(true)] out EnvelopePayload? payload)
    {
        payload = null;
        if (data.Length < 2 || data[0] != FormatVersion)
        {
            return false;
        }

        int masterKeyIdLength = data[1];
        int offset = 2 + masterKeyIdLength;
        if (masterKeyIdLength == 0 || data.Length < offset + 2)
        {
            return false;
        }

        string masterKeyId;
        try
        {
            masterKeyId = StrictUtf8.GetString(data.Slice(2, masterKeyIdLength));
        }
        catch (DecoderFallbackException)
        {
            return false;
        }

        int wrappedKeyLength = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(offset, 2));
        offset += 2;
        if (string.IsNullOrWhiteSpace(masterKeyId)
            || wrappedKeyLength is 0 or > MaxWrappedKeyLength
            || data.Length < offset + wrappedKeyLength + EncryptedPayload.NonceSize + EncryptedPayload.TagSize)
        {
            return false;
        }

        ReadOnlySpan<byte> wrappedKey = data.Slice(offset, wrappedKeyLength);
        offset += wrappedKeyLength;

        payload = new EnvelopePayload(
            masterKeyId,
            wrappedKey,
            data.Slice(offset, EncryptedPayload.NonceSize),
            data[(offset + EncryptedPayload.NonceSize + EncryptedPayload.TagSize)..],
            data.Slice(offset + EncryptedPayload.NonceSize, EncryptedPayload.TagSize));
        return true;
    }

    /// <summary>Reads a payload written by <see cref="ToString"/>. Never throws for malformed input.</summary>
    /// <param name="encoded">The Base64Url-encoded payload.</param>
    /// <param name="payload">The payload, when successful.</param>
    /// <returns><see langword="true"/> when <paramref name="encoded"/> is a well-formed payload.</returns>
    public static bool TryParse([NotNullWhen(true)] string? encoded, [NotNullWhen(true)] out EnvelopePayload? payload)
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

    /// <summary>The version, master key id and wrapped key, length-prefixed; authenticated as part of the associated data.</summary>
    internal static byte[] WriteHeader(string masterKeyId, ReadOnlySpan<byte> wrappedKey)
    {
        int idLength = Encoding.UTF8.GetByteCount(masterKeyId);
        byte[] header = new byte[2 + idLength + 2 + wrappedKey.Length];
        Span<byte> span = header;

        span[0] = FormatVersion;
        span[1] = (byte)idLength;
        Encoding.UTF8.GetBytes(masterKeyId, span[2..]);
        BinaryPrimitives.WriteUInt16BigEndian(span.Slice(2 + idLength, 2), (ushort)wrappedKey.Length);
        wrappedKey.CopyTo(span[(4 + idLength)..]);
        return header;
    }
}

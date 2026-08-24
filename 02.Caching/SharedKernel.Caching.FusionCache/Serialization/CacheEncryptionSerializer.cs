using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using SharedKernel.Cryptography.Symmetric;
using SharedKernel.Primitives.Results;
using ZiggyCreatures.Caching.Fusion.Serialization;

namespace SharedKernel.Caching.FusionCache.Serialization;

/// <summary>
/// A decorator over <see cref="IFusionCacheSerializer"/> that applies opt-in AES-GCM authenticated
/// encryption to serialized payloads before they reach the FusionCache L1/L2 pipeline.
/// </summary>
/// <remarks>
/// <para>
/// Every payload is encrypted with the currently-registered <see cref="ISymmetricEncryptionService"/>
/// (<c>01.Core/SharedKernel.Cryptography</c>, AES-256-GCM) and prefixed with the two-byte magic
/// marker <c>0x45 0x4E</c> ("EN" in ASCII) — deliberately distinct from <c>BrotliCacheSerializer</c>'s
/// <c>0x42 0x52</c> ("BR") so both decorators can coexist and correctly identify their own frame. On
/// read, the magic prefix is detected and the payload is decrypted transparently before being
/// forwarded to the inner serializer.
/// </para>
/// <para>
/// Payloads without the magic prefix — including any payload written before encryption was
/// enabled — are forwarded directly to the inner serializer without modification, ensuring full
/// backward-compatibility with existing cached data.
/// </para>
/// <para>
/// Unlike Brotli compression (L2-only), this decorator applies uniformly to both L1 and L2, since
/// FusionCache uses one <see cref="IFusionCacheSerializer"/> for both layers and this decorator
/// shape has no mechanism to apply itself only at the L2 boundary. This is an accepted,
/// documented, harmless-but-slightly-wasteful side effect on L1 — not a defect to work around.
/// </para>
/// <para>
/// A tampered or corrupted ciphertext fails authenticated AES-GCM decryption loudly (a thrown
/// <see cref="CryptographicException"/>) rather than silently deserializing corrupted data.
/// </para>
/// <para>
/// Register via <c>ICachingBuilder.AddCacheEncryption()</c> at startup. When composed with Brotli
/// compression, <c>AddBrotliCompression()</c> must be called first so this decorator becomes the
/// outermost wrapper — producing compress-then-encrypt on write and decrypt-then-decompress on
/// read.
/// </para>
/// </remarks>
internal sealed class CacheEncryptionSerializer : IFusionCacheSerializer
{
    // Magic bytes prepended to every encrypted payload: ASCII "EN" (0x45, 0x4E).
    // Deliberately distinct from BrotliCacheSerializer's 0x42/0x52 ("BR") magic bytes.
    private const byte MagicByte0 = 0x45;
    private const byte MagicByte1 = 0x4E;

    private const int LengthPrefixSize = sizeof(int);

    private readonly IFusionCacheSerializer _inner;
    private readonly ISymmetricEncryptionService _encryptionService;

    /// <summary>
    /// Initialises a new instance of <see cref="CacheEncryptionSerializer"/>.
    /// </summary>
    /// <param name="inner">
    /// The base serializer to delegate to. This is whichever <see cref="IFusionCacheSerializer"/>
    /// was registered before <c>AddCacheEncryption()</c> was called — the base STJ serializer, or a
    /// <c>BrotliCacheSerializer</c> when compression is composed underneath.
    /// </param>
    /// <param name="encryptionService">
    /// Performs the actual AES-GCM encryption/decryption. Key material is entirely owned and
    /// resolved by this service — never duplicated here.
    /// </param>
    internal CacheEncryptionSerializer(
        IFusionCacheSerializer inner,
        ISymmetricEncryptionService encryptionService)
    {
        _inner = inner;
        _encryptionService = encryptionService;
    }

    /// <inheritdoc />
    public byte[] Serialize<T>(T? obj)
    {
        byte[] payload = _inner.Serialize(obj);
        return Encrypt(payload);
    }

    /// <inheritdoc />
    public T? Deserialize<T>(byte[] data)
    {
        byte[] decrypted = DecryptIfEncrypted(data);
        return _inner.Deserialize<T>(decrypted);
    }

    /// <inheritdoc />
    public async ValueTask<byte[]> SerializeAsync<T>(T? obj, CancellationToken token = default)
    {
        byte[] payload = await _inner.SerializeAsync(obj, token).ConfigureAwait(false);
        return Encrypt(payload);
    }

    /// <inheritdoc />
    public async ValueTask<T?> DeserializeAsync<T>(byte[] data, CancellationToken token = default)
    {
        byte[] decrypted = DecryptIfEncrypted(data);
        return await _inner.DeserializeAsync<T>(decrypted, token).ConfigureAwait(false);
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Encrypts <paramref name="payload"/> and packs it, magic-prefixed, into a single flat byte
    /// array. Every value is encrypted unconditionally — unlike Brotli's size threshold, AES-GCM's
    /// fixed per-value overhead is worth paying unconditionally for confidentiality once a caller
    /// has explicitly opted in.
    /// </summary>
    private byte[] Encrypt(byte[] payload)
    {
        EncryptedPayload encrypted = _encryptionService.Encrypt(payload);
        return Pack(encrypted);
    }

    /// <summary>
    /// Detects the magic prefix and decrypts the payload if present; otherwise returns the
    /// original byte array unchanged so the inner serializer can handle it directly.
    /// </summary>
    private byte[] DecryptIfEncrypted(byte[] data)
    {
        if (data.Length < 2 || data[0] != MagicByte0 || data[1] != MagicByte1)
            return data;

        EncryptedPayload payload = Unpack(data);
        Result<byte[]> result = _encryptionService.Decrypt(payload);

        if (result.IsFailure)
        {
            throw new CryptographicException(
                $"Failed to decrypt cache entry: {result.Error.Message}");
        }

        return result.Value;
    }

    /// <summary>
    /// Packs magic bytes + length-prefixed KeyId + length-prefixed Nonce + length-prefixed Tag +
    /// Ciphertext into a single flat byte array. Every component is explicitly length-prefixed
    /// rather than assumed fixed-size, since <see cref="EncryptedPayload"/> itself makes no such
    /// guarantee — that is an implementation detail of the concrete
    /// <see cref="ISymmetricEncryptionService"/> in use.
    /// </summary>
    private static byte[] Pack(EncryptedPayload payload)
    {
        byte[] keyIdBytes = Encoding.UTF8.GetBytes(payload.KeyId);

        int length = 2 // magic bytes
            + (LengthPrefixSize * 3)
            + keyIdBytes.Length
            + payload.Nonce.Length
            + payload.Tag.Length
            + payload.Ciphertext.Length;

        byte[] buffer = new byte[length];
        Span<byte> span = buffer;

        span[0] = MagicByte0;
        span[1] = MagicByte1;
        int offset = 2;

        offset = WriteSegment(span, offset, keyIdBytes);
        offset = WriteSegment(span, offset, payload.Nonce);
        offset = WriteSegment(span, offset, payload.Tag);
        payload.Ciphertext.CopyTo(span[offset..]);

        return buffer;
    }

    /// <summary>
    /// Decodes a byte array previously produced by <see cref="Pack"/> (magic bytes already
    /// confirmed present and skipped by the caller) back into an <see cref="EncryptedPayload"/>.
    /// </summary>
    /// <exception cref="FormatException">
    /// Thrown when the data following the magic prefix is not a valid encoding produced by
    /// <see cref="Pack"/> — deliberately loud, never a silently wrong result.
    /// </exception>
    private static EncryptedPayload Unpack(byte[] data)
    {
        ReadOnlySpan<byte> span = data;
        int offset = 2; // skip magic bytes, already validated by the caller

        byte[] keyIdBytes = ReadSegment(span, ref offset);
        byte[] nonce = ReadSegment(span, ref offset);
        byte[] tag = ReadSegment(span, ref offset);
        byte[] ciphertext = span[offset..].ToArray();

        return new EncryptedPayload(Encoding.UTF8.GetString(keyIdBytes), nonce, ciphertext, tag);
    }

    private static int WriteSegment(Span<byte> span, int offset, byte[] segment)
    {
        BinaryPrimitives.WriteInt32BigEndian(span.Slice(offset, LengthPrefixSize), segment.Length);
        offset += LengthPrefixSize;
        segment.CopyTo(span[offset..]);
        return offset + segment.Length;
    }

    private static byte[] ReadSegment(ReadOnlySpan<byte> span, ref int offset)
    {
        if (offset + LengthPrefixSize > span.Length)
            throw new FormatException("Malformed encrypted cache payload: truncated length prefix.");

        int length = BinaryPrimitives.ReadInt32BigEndian(span.Slice(offset, LengthPrefixSize));
        offset += LengthPrefixSize;

        if (length < 0 || offset + length > span.Length)
            throw new FormatException("Malformed encrypted cache payload: truncated segment.");

        byte[] segment = span.Slice(offset, length).ToArray();
        offset += length;
        return segment;
    }
}

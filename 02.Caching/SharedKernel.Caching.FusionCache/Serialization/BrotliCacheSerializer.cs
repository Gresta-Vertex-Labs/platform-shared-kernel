using System.Buffers;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using SharedKernel.Caching.FusionCache.Extensions;
using ZiggyCreatures.Caching.Fusion.Serialization;

namespace SharedKernel.Caching.FusionCache.Serialization;

/// <summary>
/// A decorator over <see cref="IFusionCacheSerializer"/> that applies opt-in Brotli compression
/// to serialized payloads before they are written to the L2 Redis distributed cache.
/// </summary>
/// <remarks>
/// <para>
/// Payloads at or above <see cref="CachingOptions.CompressionOptions.L2ThresholdBytes"/> are
/// compressed with <see cref="BrotliEncoder"/> and prefixed with the two-byte magic marker
/// <c>0x42 0x52</c> ("BR" in ASCII). On read, the magic prefix is detected and the payload is
/// decompressed transparently before being forwarded to the inner serializer.
/// </para>
/// <para>
/// Payloads below the threshold, and any payloads written before compression was enabled, are
/// forwarded directly to the inner serializer without modification — ensuring full
/// backward-compatibility with existing cached data.
/// </para>
/// <para>
/// L1 in-process cache entries are never affected by this serializer; it only executes on the
/// L2 Redis path.
/// </para>
/// <para>
/// Register via <c>ICachingBuilder.AddBrotliCompression()</c> at startup.
/// </para>
/// </remarks>
internal sealed class BrotliCacheSerializer : IFusionCacheSerializer
{
    // Magic bytes prepended to every compressed payload: ASCII "BR" (0x42, 0x52).
    private const byte MagicByte0 = 0x42;
    private const byte MagicByte1 = 0x52;

    private readonly IFusionCacheSerializer _inner;
    private readonly CachingOptions.CompressionOptions _options;

    /// <summary>
    /// Initialises a new instance of <see cref="BrotliCacheSerializer"/>.
    /// </summary>
    /// <param name="inner">
    /// The base serializer to delegate to. Must not be <see langword="null"/>.
    /// This should be the STJ serializer registered by <c>AddSharedKernelCaching</c>.
    /// </param>
    /// <param name="options">
    /// Compression options controlling the threshold and compression level.
    /// </param>
    internal BrotliCacheSerializer(
        IFusionCacheSerializer inner,
        CachingOptions.CompressionOptions options)
    {
        _inner = inner;
        _options = options;
    }

    /// <inheritdoc />
    public byte[] Serialize<T>(T? obj)
    {
        var payload = _inner.Serialize(obj);
        return CompressIfAboveThreshold(payload);
    }

    /// <inheritdoc />
    public T? Deserialize<T>(byte[] data)
    {
        var decompressed = DecompressIfCompressed(data);
        return _inner.Deserialize<T>(decompressed);
    }

    /// <inheritdoc />
    public async ValueTask<byte[]> SerializeAsync<T>(T? obj, CancellationToken token = default)
    {
        var payload = await _inner.SerializeAsync(obj, token).ConfigureAwait(false);
        return CompressIfAboveThreshold(payload);
    }

    /// <inheritdoc />
    public async ValueTask<T?> DeserializeAsync<T>(byte[] data, CancellationToken token = default)
    {
        var decompressed = DecompressIfCompressed(data);
        return await _inner.DeserializeAsync<T>(decompressed, token).ConfigureAwait(false);
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Maps <see cref="CompressionLevel"/> to a Brotli quality integer (0–11).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int ToQuality(CompressionLevel level) => level switch
    {
        CompressionLevel.NoCompression => 0,
        CompressionLevel.Fastest => 1,
        CompressionLevel.Optimal => 11,
        CompressionLevel.SmallestSize => 11,
        _ => 1,
    };

    /// <summary>
    /// Compresses <paramref name="payload"/> if its length meets the threshold; otherwise
    /// returns the original byte array unchanged.
    /// </summary>
    private byte[] CompressIfAboveThreshold(byte[] payload)
    {
        if (payload.Length < _options.L2ThresholdBytes)
            return payload;

        // Rent a buffer large enough for the magic prefix + worst-case Brotli output.
        int maxCompressedLength = BrotliEncoder.GetMaxCompressedLength(payload.Length);
        int rentSize = maxCompressedLength + 2; // +2 for magic bytes
        byte[] rented = ArrayPool<byte>.Shared.Rent(rentSize);

        try
        {
            rented[0] = MagicByte0;
            rented[1] = MagicByte1;

            bool compressed = BrotliEncoder.TryCompress(
                payload,
                rented.AsSpan(2),
                out int bytesWritten,
                quality: ToQuality(_options.Level),
                window: 22); // default Brotli window size

            if (!compressed)
            {
                // Compression failed (shouldn't happen for correct buffer sizing) — passthrough.
                return payload;
            }

            // Copy only the used portion (magic bytes + compressed data) to a new array.
            int totalLength = bytesWritten + 2;
            var result = new byte[totalLength];
            rented.AsSpan(0, totalLength).CopyTo(result);
            return result;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    /// <summary>
    /// Detects the magic prefix and decompresses the payload if present; otherwise returns
    /// the original byte array unchanged so the inner serializer can handle it directly.
    /// </summary>
    private static byte[] DecompressIfCompressed(byte[] data)
    {
        if (data.Length < 2 || data[0] != MagicByte0 || data[1] != MagicByte1)
            return data;

        // Decompress using BrotliStream because we don't know the output size ahead of time.
        using var inputStream = new MemoryStream(data, 2, data.Length - 2, writable: false);
        using var brotliStream = new BrotliStream(inputStream, CompressionMode.Decompress, leaveOpen: false);
        using var outputStream = new MemoryStream();

        brotliStream.CopyTo(outputStream);
        return outputStream.ToArray();
    }
}

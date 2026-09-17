using System.Buffers;
using System.IO.Compression;
using System.Runtime.CompilerServices;

namespace SharedKernel.Caching.FusionCache.Serialization;

/// <summary>
/// The Brotli codec shared by <see cref="BrotliCacheSerializer"/> and
/// <c>Encryption.EncryptedCacheService</c>: a two-byte "BR" marker followed by the compressed bytes.
/// Payloads without the marker are returned unchanged, so uncompressed entries stay readable.
/// </summary>
internal static class BrotliPayloadCodec
{
    // Magic bytes prepended to every compressed payload: ASCII "BR" (0x42, 0x52).
    private const byte MagicByte0 = 0x42;
    private const byte MagicByte1 = 0x52;

    /// <summary>
    /// Maps <see cref="CompressionLevel"/> to a Brotli quality integer (0–11).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static int ToQuality(CompressionLevel level) => level switch
    {
        CompressionLevel.NoCompression => 0,
        CompressionLevel.Fastest => 1,
        CompressionLevel.Optimal => 11,
        CompressionLevel.SmallestSize => 11,
        _ => 1,
    };

    /// <summary>
    /// Compresses <paramref name="payload"/> if its length is at least <paramref name="thresholdBytes"/>;
    /// otherwise returns <paramref name="payload"/> unchanged. Pass <c>thresholdBytes: 0</c> to compress
    /// unconditionally.
    /// </summary>
    internal static byte[] Compress(byte[] payload, int thresholdBytes, CompressionLevel level)
    {
        if (payload.Length < thresholdBytes)
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
                quality: ToQuality(level),
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
    /// Detects the magic prefix and decompresses <paramref name="data"/> if present; otherwise
    /// returns <paramref name="data"/> unchanged.
    /// </summary>
    internal static byte[] Decompress(byte[] data)
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

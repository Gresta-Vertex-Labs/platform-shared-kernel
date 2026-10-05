using System.Buffers;
using System.IO.Compression;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Compression.Internal;

/// <summary>
/// Everything <see cref="BrotliPayloadCompressor"/> and <see cref="GZipPayloadCompressor"/> do, in one
/// place, parameterised by <see cref="CodecSettings"/>.
/// </summary>
/// <remarks>
/// The two public compressors differ only in which <see cref="Stream"/> they construct, so they are
/// thin wrappers over this type rather than two copies of the same logic — which is how they came to
/// need the same fixes twice. Kept internal so the public surface stays the interface and its two
/// implementations; a public abstract base would have to be public for them to derive from it.
/// </remarks>
internal static class CompressionCodec
{
    private const int CopyBufferSize = 81920;

    /// <summary>Upper bound on how much output is pre-allocated from a payload's own claim about its size.</summary>
    private const int MaxPreallocation = 4 * 1024 * 1024;

    internal static byte[] Compress(in CodecSettings settings, ReadOnlySpan<byte> data)
    {
        using var output = new MemoryStream(EstimateCompressedCapacity(settings, data.Length));
        WriteHeader(settings, output, data.Length);

        using (var compressor = CreateCompressor(settings, output))
        {
            compressor.Write(data);
        }

        return output.ToArray();
    }

    internal static void Compress(in CodecSettings settings, ReadOnlySpan<byte> data, IBufferWriter<byte> output)
    {
        if (settings.Framing == CompressionFraming.Framed)
            CompressionFrame.Write(output, settings.Algorithm, data.Length);

        using var sink = new BufferWriterStream(output);
        using var compressor = CreateCompressor(settings, sink);
        compressor.Write(data);
    }

    internal static void Compress(in CodecSettings settings, Stream input, Stream output)
    {
        var declared = DeclaredLengthOf(input);
        var headerPosition = output.CanSeek ? output.Position : -1L;
        WriteHeader(settings, output, declared);

        long copied;
        using (var compressor = CreateCompressor(settings, output))
        {
            copied = Copy(input, compressor);
        }

        if (NeedsLengthPatch(settings, declared, headerPosition))
            CompressionFrame.PatchLength(output, headerPosition, copied);
    }

    internal static async ValueTask CompressAsync(
        CodecSettings settings,
        Stream input,
        Stream output,
        CancellationToken cancellationToken)
    {
        var declared = DeclaredLengthOf(input);
        var headerPosition = output.CanSeek ? output.Position : -1L;
        if (settings.Framing == CompressionFraming.Framed)
            await CompressionFrame.WriteAsync(output, settings.Algorithm, declared, cancellationToken).ConfigureAwait(false);

        long copied;
        var compressor = CreateCompressor(settings, output);
        await using (compressor.ConfigureAwait(false))
        {
            copied = await CopyAsync(input, compressor, cancellationToken).ConfigureAwait(false);
        }

        if (NeedsLengthPatch(settings, declared, headerPosition))
            await CompressionFrame.PatchLengthAsync(output, headerPosition, copied, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The common path, and the only one that needs no copy of the compressed bytes: the caller already
    /// holds an array, so the frame is skipped by offset and the array is read in place.
    /// </summary>
    internal static Result<byte[]> Decompress(in CodecSettings settings, byte[] compressed)
    {
        var header = ReadHeader(settings, compressed, out var offset);
        if (header.IsFailure)
            return header.Error;

        var declared = header.Value;
        if (ExceedsLimit(settings, declared) is { } tooLarge)
            return tooLarge;

        var bodyLength = compressed.Length - offset;
        using var input = new MemoryStream(compressed, offset, bodyLength, writable: false);
        using var output = new MemoryStream(PreallocationFor(declared, bodyLength));

        var result = DecompressCore(settings, input, output, declared);
        return result.IsFailure ? result.Error : output.ToArray();
    }

    internal static Result<byte[]> Decompress(in CodecSettings settings, ReadOnlySpan<byte> compressed)
    {
        var header = ReadHeader(settings, ref compressed);
        if (header.IsFailure)
            return header.Error;

        var declared = header.Value;
        using var output = new MemoryStream(PreallocationFor(declared, compressed.Length));
        var result = DecompressBody(settings, compressed, output, declared);
        return result.IsFailure ? result.Error : output.ToArray();
    }

    internal static Result Decompress(in CodecSettings settings, ReadOnlySpan<byte> compressed, IBufferWriter<byte> output)
    {
        var header = ReadHeader(settings, ref compressed);
        if (header.IsFailure)
            return header.Error;

        using var sink = new BufferWriterStream(output);
        return DecompressBody(settings, compressed, sink, header.Value);
    }

    internal static Result Decompress(in CodecSettings settings, Stream input, Stream output)
    {
        var declared = CompressionFrame.UnknownLength;
        if (settings.Framing == CompressionFraming.Framed)
        {
            var header = CompressionFrame.Read(input, settings.Algorithm);
            if (header.IsFailure)
                return header.Error;

            declared = header.Value;
        }

        if (ExceedsLimit(settings, declared) is { } tooLarge)
            return tooLarge;

        return DecompressCore(settings, input, output, declared);
    }

    internal static async ValueTask<Result> DecompressAsync(
        CodecSettings settings,
        Stream input,
        Stream output,
        CancellationToken cancellationToken)
    {
        var declared = CompressionFrame.UnknownLength;
        if (settings.Framing == CompressionFraming.Framed)
        {
            var header = await CompressionFrame.ReadAsync(input, settings.Algorithm, cancellationToken).ConfigureAwait(false);
            if (header.IsFailure)
                return header.Error;

            declared = header.Value;
        }

        if (ExceedsLimit(settings, declared) is { } tooLarge)
            return tooLarge;

        try
        {
            var decompressor = CreateDecompressor(settings, input);
            await using (decompressor.ConfigureAwait(false))
            {
                var written = await CopyCappedAsync(decompressor, output, settings.MaxDecompressedSize, cancellationToken)
                    .ConfigureAwait(false);
                return written.IsFailure ? written.Error : VerifyLength(declared, written.Value);
            }
        }
        catch (Exception ex) when (IsCorruptPayload(ex))
        {
            return CompressionErrors.Corrupt();
        }
    }

    /// <summary>
    /// Validates the frame header at the front of <paramref name="payload"/> and reports how many bytes to
    /// skip to reach the compressed body. Returns the recorded uncompressed length, or
    /// <see cref="CompressionFrame.UnknownLength"/> in raw mode.
    /// </summary>
    private static Result<long> ReadHeader(in CodecSettings settings, byte[] payload, out int offset)
    {
        offset = 0;
        if (settings.Framing != CompressionFraming.Framed)
            return CompressionFrame.UnknownLength;

        var header = CompressionFrame.Read(payload, settings.Algorithm);
        if (header.IsSuccess)
            offset = CompressionFrame.Size;

        return header;
    }

    /// <summary>
    /// Span counterpart of <see cref="ReadHeader(in CodecSettings, byte[], out int)"/>, advancing
    /// <paramref name="payload"/> past the header instead of reporting an offset.
    /// </summary>
    private static Result<long> ReadHeader(in CodecSettings settings, ref ReadOnlySpan<byte> payload)
    {
        if (settings.Framing != CompressionFraming.Framed)
            return CompressionFrame.UnknownLength;

        var header = CompressionFrame.Read(payload, settings.Algorithm);
        if (header.IsSuccess)
            payload = payload[CompressionFrame.Size..];

        return header;
    }

    private static Result DecompressBody(
        in CodecSettings settings,
        ReadOnlySpan<byte> body,
        Stream output,
        long declared)
    {
        if (ExceedsLimit(settings, declared) is { } tooLarge)
            return tooLarge;

        // The compression streams only read from a Stream, and no Stream wraps a span — so the compressed
        // bytes (the smaller side) are copied into a pooled array, while the decompressed bytes (the larger
        // side) go straight to the caller's destination. The byte[] overload avoids this copy entirely.
        var rented = ArrayPool<byte>.Shared.Rent(Math.Max(body.Length, 1));
        try
        {
            body.CopyTo(rented);
            using var input = new MemoryStream(rented, 0, body.Length, writable: false);
            return DecompressCore(settings, input, output, declared);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    /// <summary>
    /// Decompresses <paramref name="input"/> into <paramref name="output"/> under the size cap, then checks
    /// the produced length against the frame's. The single place corrupt payloads are turned into a failure.
    /// </summary>
    private static Result DecompressCore(in CodecSettings settings, Stream input, Stream output, long declared)
    {
        try
        {
            using var decompressor = CreateDecompressor(settings, input);
            var written = CopyCapped(decompressor, output, settings.MaxDecompressedSize);
            return written.IsFailure ? written.Error : VerifyLength(declared, written.Value);
        }
        catch (Exception ex) when (IsCorruptPayload(ex))
        {
            return CompressionErrors.Corrupt();
        }
    }

    /// <summary>Copies until the source ends, refusing to hand on more than <paramref name="limit"/> bytes.</summary>
    private static Result<long> CopyCapped(Stream source, Stream destination, long limit)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(CopyBufferSize);
        try
        {
            long total = 0;
            int read;
            while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
            {
                total += read;
                if (total > limit)
                    return CompressionErrors.TooLarge(limit);

                destination.Write(buffer, 0, read);
            }

            return total;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <inheritdoc cref="CopyCapped(Stream, Stream, long)"/>
    private static async ValueTask<Result<long>> CopyCappedAsync(
        Stream source,
        Stream destination,
        long limit,
        CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(CopyBufferSize);
        try
        {
            long total = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                total += read;
                if (total > limit)
                    return CompressionErrors.TooLarge(limit);

                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            }

            return total;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static long Copy(Stream source, Stream destination)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(CopyBufferSize);
        try
        {
            long total = 0;
            int read;
            while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
            {
                total += read;
                destination.Write(buffer, 0, read);
            }

            return total;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static async ValueTask<long> CopyAsync(Stream source, Stream destination, CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(CopyBufferSize);
        try
        {
            long total = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                total += read;
                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            }

            return total;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static Result VerifyLength(long declared, long actual) =>
        declared != CompressionFrame.UnknownLength && declared != actual
            ? CompressionErrors.Truncated(declared, actual)
            : Result.Success();

    private static Error? ExceedsLimit(in CodecSettings settings, long declared) =>
        declared > settings.MaxDecompressedSize ? CompressionErrors.TooLarge(settings.MaxDecompressedSize) : null;

    private static void WriteHeader(in CodecSettings settings, Stream output, long uncompressedLength)
    {
        if (settings.Framing == CompressionFraming.Framed)
            CompressionFrame.Write(output, settings.Algorithm, uncompressedLength);
    }

    private static bool NeedsLengthPatch(in CodecSettings settings, long declared, long headerPosition) =>
        settings.Framing == CompressionFraming.Framed
        && declared == CompressionFrame.UnknownLength
        && headerPosition >= 0;

    /// <summary>The bytes remaining in <paramref name="input"/>, or <see cref="CompressionFrame.UnknownLength"/>.</summary>
    private static long DeclaredLengthOf(Stream input)
    {
        if (!input.CanSeek)
            return CompressionFrame.UnknownLength;

        try
        {
            var remaining = input.Length - input.Position;
            return remaining >= 0 ? remaining : CompressionFrame.UnknownLength;
        }
        catch (NotSupportedException)
        {
            // CanSeek said yes but Length still refuses — treat it as unknown rather than failing the write.
            return CompressionFrame.UnknownLength;
        }
    }

    private static int EstimateCompressedCapacity(in CodecSettings settings, int uncompressedLength)
    {
        var header = settings.Framing == CompressionFraming.Framed ? CompressionFrame.Size : 0;
        return header + (int)Math.Min(Math.Max(64L, uncompressedLength / 2L), MaxPreallocation);
    }

    /// <summary>
    /// How much output to pre-allocate. Never sized from the frame's recorded length alone: a 13-byte payload
    /// claiming 64 MiB would otherwise allocate 64 MiB before decompressing a single byte. All arithmetic is
    /// in <see cref="long"/>, so a large compressed length cannot overflow into a nonsense hint.
    /// </summary>
    private static int PreallocationFor(long declared, int compressedLength) =>
        declared > 0
            ? (int)Math.Min(declared, MaxPreallocation)
            : (int)Math.Clamp(compressedLength * 4L, CopyBufferSize, MaxPreallocation);

    private static bool IsCorruptPayload(Exception exception) =>
        exception is InvalidDataException or InvalidOperationException;

    private static Stream CreateCompressor(in CodecSettings settings, Stream output) =>
        settings.Algorithm == CompressionAlgorithm.Brotli
            ? new BrotliStream(output, settings.Level, leaveOpen: true)
            : new GZipStream(output, settings.Level, leaveOpen: true);

    private static Stream CreateDecompressor(in CodecSettings settings, Stream input) =>
        settings.Algorithm == CompressionAlgorithm.Brotli
            ? new BrotliStream(input, CompressionMode.Decompress, leaveOpen: true)
            : new GZipStream(input, CompressionMode.Decompress, leaveOpen: true);
}

/// <summary>The per-compressor configuration <see cref="CompressionCodec"/> operates under.</summary>
internal readonly record struct CodecSettings(
    CompressionAlgorithm Algorithm,
    CompressionFraming Framing,
    CompressionLevel Level,
    long MaxDecompressedSize);

using System.Buffers;
using System.Buffers.Binary;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Compression.Internal;

/// <summary>
/// Reads and writes the 13-byte header that precedes a <see cref="CompressionFraming.Framed"/>
/// payload.
/// </summary>
/// <remarks>
/// <para>
/// Layout: <c>"SKC"</c> magic (3 bytes), format version (1), <see cref="CompressionAlgorithm"/> (1),
/// uncompressed length as little-endian <see cref="long"/> (8).
/// </para>
/// <para>
/// <b>Why the length is in a header and not a trailer.</b> A trailer would let a non-seekable input
/// stream record its length after the fact, but it cannot be read back: both
/// <c>BrotliStream</c> and <c>GZipStream</c> buffer ahead and consume bytes past the end of their own
/// data — measured at 8 of 8 trailer bytes swallowed for both algorithms, on seekable and
/// non-seekable inputs alike. Anything after the compressed bytes is unrecoverable, so the length
/// goes first.
/// </para>
/// </remarks>
internal static class CompressionFrame
{
    /// <summary>Total header size in bytes.</summary>
    internal const int Size = 13;

    /// <summary>The value recorded when the uncompressed length could not be determined up front.</summary>
    internal const long UnknownLength = -1;

    /// <summary>Byte offset of the length field within the header.</summary>
    internal const int LengthOffset = 5;

    private const byte Magic0 = (byte)'S';
    private const byte Magic1 = (byte)'K';
    private const byte Magic2 = (byte)'C';
    private const byte CurrentVersion = 1;

    internal static void Write(Span<byte> destination, CompressionAlgorithm algorithm, long uncompressedLength)
    {
        destination[0] = Magic0;
        destination[1] = Magic1;
        destination[2] = Magic2;
        destination[3] = CurrentVersion;
        destination[4] = (byte)algorithm;
        BinaryPrimitives.WriteInt64LittleEndian(destination[LengthOffset..], uncompressedLength);
    }

    internal static void Write(Stream destination, CompressionAlgorithm algorithm, long uncompressedLength)
    {
        Span<byte> header = stackalloc byte[Size];
        Write(header, algorithm, uncompressedLength);
        destination.Write(header);
    }

    internal static void Write(IBufferWriter<byte> destination, CompressionAlgorithm algorithm, long uncompressedLength)
    {
        Write(destination.GetSpan(Size), algorithm, uncompressedLength);
        destination.Advance(Size);
    }

    internal static async ValueTask WriteAsync(
        Stream destination,
        CompressionAlgorithm algorithm,
        long uncompressedLength,
        CancellationToken cancellationToken)
    {
        var header = new byte[Size];
        Write(header, algorithm, uncompressedLength);
        await destination.WriteAsync(header, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Rewrites the length field of a header already written at <paramref name="headerPosition"/> in a
    /// seekable stream, then restores the stream's position.
    /// </summary>
    internal static void PatchLength(Stream destination, long headerPosition, long uncompressedLength)
    {
        Span<byte> length = stackalloc byte[sizeof(long)];
        BinaryPrimitives.WriteInt64LittleEndian(length, uncompressedLength);

        var resume = destination.Position;
        destination.Position = headerPosition + LengthOffset;
        destination.Write(length);
        destination.Position = resume;
    }

    /// <inheritdoc cref="PatchLength(Stream, long, long)"/>
    internal static async ValueTask PatchLengthAsync(
        Stream destination,
        long headerPosition,
        long uncompressedLength,
        CancellationToken cancellationToken)
    {
        var length = new byte[sizeof(long)];
        BinaryPrimitives.WriteInt64LittleEndian(length, uncompressedLength);

        var resume = destination.Position;
        destination.Position = headerPosition + LengthOffset;
        await destination.WriteAsync(length, cancellationToken).ConfigureAwait(false);
        destination.Position = resume;
    }

    /// <summary>
    /// Validates a header and returns the uncompressed length it records, or
    /// <see cref="UnknownLength"/> when the writer could not determine one.
    /// </summary>
    internal static Result<long> Read(ReadOnlySpan<byte> header, CompressionAlgorithm expected)
    {
        if (header.Length < Size)
            return CompressionErrors.Malformed();

        if (header[0] != Magic0 || header[1] != Magic1 || header[2] != Magic2)
            return CompressionErrors.Malformed();

        if (header[3] != CurrentVersion)
            return CompressionErrors.UnsupportedVersion(header[3]);

        var algorithm = (CompressionAlgorithm)header[4];
        if (algorithm != expected)
            return CompressionErrors.AlgorithmMismatch(algorithm, expected);

        var length = BinaryPrimitives.ReadInt64LittleEndian(header[LengthOffset..]);
        return length < UnknownLength ? CompressionErrors.Malformed() : length;
    }

    /// <summary>Reads and validates a header from the front of <paramref name="source"/>.</summary>
    internal static Result<long> Read(Stream source, CompressionAlgorithm expected)
    {
        Span<byte> header = stackalloc byte[Size];
        return source.ReadAtLeast(header, Size, throwOnEndOfStream: false) < Size
            ? CompressionErrors.Malformed()
            : Read(header, expected);
    }

    /// <inheritdoc cref="Read(Stream, CompressionAlgorithm)"/>
    internal static async ValueTask<Result<long>> ReadAsync(
        Stream source,
        CompressionAlgorithm expected,
        CancellationToken cancellationToken)
    {
        var header = new byte[Size];
        var read = await source.ReadAtLeastAsync(header, Size, throwOnEndOfStream: false, cancellationToken)
            .ConfigureAwait(false);
        return read < Size ? CompressionErrors.Malformed() : Read(header, expected);
    }
}

/// <summary>Failure results shared by both compressors.</summary>
internal static class CompressionErrors
{
    internal static Error Corrupt() =>
        Error.Validation(
            CompressionErrorCodes.DecompressionFailed,
            "Decompression failed: the compressed payload is corrupt, or was produced by a different algorithm.");

    internal static Error Malformed() =>
        Error.Validation(
            CompressionErrorCodes.MalformedPayload,
            "The payload is not a SharedKernel compression frame. A payload written by a raw compressor must be "
                + "read by a raw compressor.");

    internal static Error UnsupportedVersion(byte version) =>
        Error.Validation(
            CompressionErrorCodes.MalformedPayload,
            $"The payload declares compression frame version {version}, which this package does not understand.");

    internal static Error AlgorithmMismatch(CompressionAlgorithm actual, CompressionAlgorithm expected) =>
        Error.Validation(
            CompressionErrorCodes.AlgorithmMismatch,
            $"The payload was compressed with {actual} but read with {expected}.");

    internal static Error Truncated(long declared, long actual) =>
        Error.Validation(
            CompressionErrorCodes.TruncatedPayload,
            actual < declared
                ? $"The payload decompressed to {actual} bytes but its frame records {declared}, so it was cut short."
                : $"The payload decompressed to {actual} bytes but its frame records {declared}, so data was appended to it.");

    internal static Error TooLarge(long limit) =>
        Error.Validation(
            CompressionErrorCodes.PayloadTooLarge,
            $"Decompression stopped: the payload expands beyond the configured maximum of {limit} bytes.");
}

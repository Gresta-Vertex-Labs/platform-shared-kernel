using System.Buffers;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Compression;

/// <summary>
/// Generic compress/decompress of an arbitrary byte payload or stream.
/// </summary>
/// <remarks>
/// <para>
/// This is the direct sibling of <c>ISymmetricEncryptionService</c>'s "general-purpose
/// encrypt/decrypt of arbitrary payloads" role in <c>SharedKernel.Cryptography</c>: same shape, same
/// zero-dependency pure-BCL constraint, orthogonal concern.
/// </para>
/// <para>
/// <b>Ordering rule — compress, then encrypt, never the reverse.</b> Compression and encryption are
/// frequently combined for the same payload (before publishing to a queue, writing to blob storage).
/// Always compress first and encrypt the compressed result second. Compressing already-encrypted
/// ciphertext wastes CPU for no size benefit, because ciphertext has no redundancy left to compress.
/// Never pass a payload that has already passed through <c>ISymmetricEncryptionService.Encrypt</c> to
/// any <c>Compress</c> overload, and never double-compress: a second pass typically <i>increases</i>
/// the size, since it adds framing to data with no redundancy left.
/// </para>
/// <para>
/// <b>Which payload a compressor can read.</b> A compressor reads back only what its own
/// <see cref="Algorithm"/> and <see cref="Framing"/> produced. Reading a framed payload with a raw
/// compressor, a raw payload with a framed one, or gzip bytes with the Brotli compressor all fail
/// with a distinct error code rather than returning wrong data — see
/// <see cref="CompressionErrorCodes"/>.
/// </para>
/// <para>
/// <b>Failure shape.</b> Compression cannot fail on valid input, so it returns bare values.
/// Decompression takes data this process did not necessarily produce, so every overload returns a
/// <see cref="Result"/> and none lets <see cref="InvalidDataException"/> escape. Every decompression
/// failure is an <see cref="SharedKernel.Primitives.Errors.ErrorType.Validation"/> error, because the
/// fault is in the supplied payload rather than in the service.
/// </para>
/// <para>
/// <b>Untrusted input.</b> Decompression is bounded by
/// <see cref="Options.CompressionOptions.MaxDecompressedSize"/> and refuses to produce more than that,
/// so a decompression bomb fails instead of exhausting memory. Compression is CPU-bound work with no
/// such bound: do not compress an unbounded caller-supplied payload on a request path without
/// limiting its size first.
/// </para>
/// <para>
/// <b>There are no asynchronous in-memory overloads, deliberately.</b> Compressing or decompressing a
/// payload already in memory is pure CPU work with no I/O to await: over a <see cref="MemoryStream"/>
/// the compression streams do the work inline and their async methods complete synchronously, so an
/// <c>async</c> overload of the <see cref="byte"/> array or span members would return an
/// already-completed task and offload nothing, while implying otherwise. The asynchronous members take
/// streams, where the I/O is real. A caller who must not occupy the current thread with a large
/// in-memory payload should hand the work to the thread pool at its own call site, where the decision is
/// visible.
/// </para>
/// <para>Implementations are stateless and safe to use concurrently from multiple threads.</para>
/// </remarks>
public interface IPayloadCompressor
{
    /// <summary>The algorithm this compressor applies.</summary>
    CompressionAlgorithm Algorithm { get; }

    /// <summary>
    /// Whether this compressor wraps its output in the platform's frame, and so whether it can detect
    /// a truncated payload when reading one back.
    /// </summary>
    CompressionFraming Framing { get; }

    /// <summary>Compresses <paramref name="data"/> and returns the compressed bytes.</summary>
    /// <param name="data">The uncompressed payload.</param>
    /// <returns>The compressed bytes, including the frame header when <see cref="Framing"/> is framed.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="data"/> is <see langword="null"/>.</exception>
    byte[] Compress(byte[] data);

    /// <summary>
    /// Compresses <paramref name="data"/> and returns the compressed bytes, without first copying the
    /// input into an array.
    /// </summary>
    /// <param name="data">The uncompressed payload. An empty span compresses to an empty payload.</param>
    /// <returns>The compressed bytes, including the frame header when <see cref="Framing"/> is framed.</returns>
    /// <remarks>
    /// Unlike <see cref="Compress(byte[])"/> this cannot reject a <see langword="null"/> argument — a
    /// null array widens silently to an empty span — so the result for one is an empty payload, not an
    /// exception.
    /// </remarks>
    byte[] Compress(ReadOnlySpan<byte> data);

    /// <summary>
    /// Compresses <paramref name="data"/> directly into <paramref name="output"/>, allocating neither
    /// an intermediate buffer nor a result array.
    /// </summary>
    /// <param name="data">The uncompressed payload.</param>
    /// <param name="output">Receives the compressed bytes. Not completed or reset by this method.</param>
    /// <exception cref="ArgumentNullException"><paramref name="output"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// The allocation-free path for a hot loop: <see cref="Compress(byte[])"/> grows an internal buffer
    /// and then copies out of it, costing roughly twice the payload.
    /// </remarks>
    void Compress(ReadOnlySpan<byte> data, IBufferWriter<byte> output);

    /// <summary>
    /// Compresses <paramref name="input"/>, writing the compressed bytes to <paramref name="output"/>.
    /// Both streams are caller-owned — neither is disposed by this method.
    /// </summary>
    /// <param name="input">
    /// The stream to read the uncompressed payload from, starting at its current position.
    /// </param>
    /// <param name="output">The stream to write the compressed bytes to.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="input"/> or <paramref name="output"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// <b>Truncation detection needs a length, and a stream may not be able to supply one.</b> When
    /// <see cref="Framing"/> is framed, the recorded length comes from <paramref name="input"/> if it
    /// is seekable, otherwise it is written back into the header afterwards if
    /// <paramref name="output"/> is seekable. When neither is — a network stream straight to a network
    /// stream — the payload records no length, and reading it back cannot detect truncation. Both are
    /// seekable for the common cases (<see cref="MemoryStream"/>, <see cref="FileStream"/>).
    /// </remarks>
    void Compress(Stream input, Stream output);

    /// <summary>Asynchronous variant of <see cref="Compress(Stream, Stream)"/>.</summary>
    /// <param name="input">The stream to read the uncompressed payload from.</param>
    /// <param name="output">The stream to write the compressed bytes to.</param>
    /// <param name="cancellationToken">A token to observe for cancellation requests.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="input"/> or <paramref name="output"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <remarks>
    /// On cancellation <paramref name="output"/> keeps whatever partial bytes were already written —
    /// this method cannot unwind a caller-owned stream. Discard it rather than reading it back; a
    /// partial write is exactly the truncated payload the frame exists to reject.
    /// </remarks>
    ValueTask CompressAsync(Stream input, Stream output, CancellationToken cancellationToken = default);

    /// <summary>Decompresses <paramref name="compressed"/> and returns the original bytes.</summary>
    /// <param name="compressed">A payload previously produced by a compressor with the same
    /// <see cref="Algorithm"/> and <see cref="Framing"/>.</param>
    /// <returns>
    /// The decompressed bytes, or a failed result carrying one of
    /// <see cref="CompressionErrorCodes.DecompressionFailed"/>,
    /// <see cref="CompressionErrorCodes.TruncatedPayload"/>,
    /// <see cref="CompressionErrorCodes.PayloadTooLarge"/>,
    /// <see cref="CompressionErrorCodes.MalformedPayload"/> or
    /// <see cref="CompressionErrorCodes.AlgorithmMismatch"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="compressed"/> is <see langword="null"/>.</exception>
    Result<byte[]> Decompress(byte[] compressed);

    /// <summary>
    /// Decompresses <paramref name="compressed"/> and returns the original bytes, without first copying
    /// the input into an array.
    /// </summary>
    /// <param name="compressed">A payload previously produced by a matching compressor.</param>
    /// <returns>The decompressed bytes, or a failed result — see <see cref="Decompress(byte[])"/>.</returns>
    Result<byte[]> Decompress(ReadOnlySpan<byte> compressed);

    /// <summary>
    /// Decompresses <paramref name="compressed"/> directly into <paramref name="output"/>.
    /// </summary>
    /// <param name="compressed">A payload previously produced by a matching compressor.</param>
    /// <param name="output">
    /// Receives the decompressed bytes. Not completed or reset by this method, and on failure it may
    /// already hold part of the payload — discard what it holds unless the result is successful.
    /// </param>
    /// <returns>A successful result, or a failed one — see <see cref="Decompress(byte[])"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="output"/> is <see langword="null"/>.</exception>
    Result Decompress(ReadOnlySpan<byte> compressed, IBufferWriter<byte> output);

    /// <summary>
    /// Decompresses <paramref name="input"/>, writing the decompressed bytes to
    /// <paramref name="output"/>. Both streams are caller-owned — neither is disposed by this method.
    /// </summary>
    /// <param name="input">The stream to read the compressed payload from.</param>
    /// <param name="output">
    /// The stream to write the decompressed bytes to. On failure it may already hold part of the
    /// payload, including up to one buffer beyond
    /// <see cref="Options.CompressionOptions.MaxDecompressedSize"/> — discard its contents unless the
    /// result is successful.
    /// </param>
    /// <returns>
    /// A successful, non-generic <see cref="Result"/> — the payload already landed in
    /// <paramref name="output"/>, so there is no value to carry — or a failed result, see
    /// <see cref="Decompress(byte[])"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="input"/> or <paramref name="output"/> is <see langword="null"/>.
    /// </exception>
    Result Decompress(Stream input, Stream output);

    /// <summary>Asynchronous variant of <see cref="Decompress(Stream, Stream)"/>.</summary>
    /// <param name="input">The stream to read the compressed payload from.</param>
    /// <param name="output">The stream to write the decompressed bytes to.</param>
    /// <param name="cancellationToken">A token to observe for cancellation requests.</param>
    /// <returns>A successful result, or a failed one — see <see cref="Decompress(Stream, Stream)"/>.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="input"/> or <paramref name="output"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    ValueTask<Result> DecompressAsync(Stream input, Stream output, CancellationToken cancellationToken = default);
}

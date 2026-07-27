using SharedKernel.Primitives.Results;

namespace SharedKernel.Compression;

/// <summary>
/// Generic compress/decompress of an arbitrary byte payload or stream.
/// </summary>
/// <remarks>
/// <para>
/// This is the direct sibling of <c>ISymmetricEncryptionService</c>'s "general-purpose encrypt/decrypt
/// of arbitrary payloads" role in <c>SharedKernel.Cryptography</c>: same shape, same zero-dependency
/// pure-BCL constraint, orthogonal concern.
/// </para>
/// <para>
/// <b>Ordering rule — compress, then encrypt, never the reverse.</b> Compression and encryption are
/// frequently combined for the same payload (e.g., before publishing to a queue or writing to blob
/// storage). Always compress first and encrypt the compressed result second. Compressing
/// already-encrypted/high-entropy ciphertext wastes CPU for no size benefit, because ciphertext has
/// no redundancy left to compress. Never pass a payload that has already passed through
/// <c>ISymmetricEncryptionService.Encrypt</c> to <see cref="Compress(byte[])"/> or its overloads.
/// </para>
/// <para>
/// Never call <see cref="Compress(byte[])"/> or its overloads on a payload that has already been
/// compressed — double-compression wastes CPU and typically <i>increases</i> the output size, since
/// compressed data has no further redundancy left to exploit and the second pass adds its own
/// framing/header overhead.
/// </para>
/// </remarks>
public interface IPayloadCompressor
{
    /// <summary>Compresses <paramref name="data"/> and returns the compressed bytes.</summary>
    /// <param name="data">The uncompressed payload.</param>
    /// <returns>The compressed bytes.</returns>
    byte[] Compress(byte[] data);

    /// <summary>
    /// Compresses <paramref name="input"/>, writing the compressed bytes to <paramref name="output"/>.
    /// Both streams are caller-owned — neither is disposed by this method.
    /// </summary>
    /// <param name="input">The stream to read the uncompressed payload from.</param>
    /// <param name="output">The stream to write the compressed bytes to.</param>
    void Compress(Stream input, Stream output);

    /// <summary>Asynchronous variant of <see cref="Compress(Stream, Stream)"/>.</summary>
    /// <param name="input">The stream to read the uncompressed payload from.</param>
    /// <param name="output">The stream to write the compressed bytes to.</param>
    /// <param name="cancellationToken">A token to observe for cancellation requests.</param>
    Task CompressAsync(Stream input, Stream output, CancellationToken cancellationToken = default);

    /// <summary>
    /// Decompresses <paramref name="compressed"/> and returns the original bytes.
    /// </summary>
    /// <param name="compressed">A previously compressed payload.</param>
    /// <returns>
    /// A successful <see cref="Result{T}"/> containing the decompressed bytes, or a failed result
    /// with <see cref="SharedKernel.Primitives.Errors.ErrorType.Unexpected"/> if
    /// <paramref name="compressed"/> is corrupt or truncated. This method never lets
    /// <see cref="InvalidDataException"/> propagate uncaught.
    /// </returns>
    Result<byte[]> Decompress(byte[] compressed);

    /// <summary>
    /// Decompresses <paramref name="input"/>, writing the decompressed bytes to
    /// <paramref name="output"/>. Both streams are caller-owned — neither is disposed by this method.
    /// </summary>
    /// <param name="input">The stream to read the compressed payload from.</param>
    /// <param name="output">The stream to write the decompressed bytes to.</param>
    /// <returns>
    /// A successful, non-generic <see cref="Result"/> — the decompressed payload already landed in
    /// <paramref name="output"/>, so there is no typed value to carry — or a failed result with
    /// <see cref="SharedKernel.Primitives.Errors.ErrorType.Unexpected"/> if <paramref name="input"/>
    /// is corrupt or truncated. This method never lets <see cref="InvalidDataException"/> propagate
    /// uncaught.
    /// </returns>
    Result Decompress(Stream input, Stream output);

    /// <summary>Asynchronous variant of <see cref="Decompress(Stream, Stream)"/>.</summary>
    /// <param name="input">The stream to read the compressed payload from.</param>
    /// <param name="output">The stream to write the decompressed bytes to.</param>
    /// <param name="cancellationToken">A token to observe for cancellation requests.</param>
    /// <returns>
    /// A successful <see cref="Result"/>, or a failed result on corrupt/truncated input — see
    /// <see cref="Decompress(Stream, Stream)"/>.
    /// </returns>
    Task<Result> DecompressAsync(Stream input, Stream output, CancellationToken cancellationToken = default);
}

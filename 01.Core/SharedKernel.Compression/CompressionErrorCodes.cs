namespace SharedKernel.Compression;

/// <summary>
/// Well-known error code constants used by <c>SharedKernel.Compression</c> failure results.
/// </summary>
/// <remarks>
/// Defined locally per the platform convention that consuming packages may add their own
/// <c>ErrorCodes</c>-style constants without forking <c>SharedKernel.Primitives</c> — mirrors
/// <c>SharedKernel.Cryptography</c>'s <c>CryptographyErrorCodes</c>. Every one of these describes
/// caller-supplied data the package refused, so each is an
/// <see cref="SharedKernel.Primitives.Errors.ErrorType.Validation"/> error and maps to HTTP 400 at
/// the boundary — never a 500, which would report bad input as a server fault.
/// </remarks>
public static class CompressionErrorCodes
{
    /// <summary>
    /// The compressed payload could not be decompressed — it is corrupt, or was produced by a
    /// different algorithm than the one that read it.
    /// </summary>
    public const string DecompressionFailed = "compression.decompression_failed";

    /// <summary>
    /// The payload decompressed cleanly but did not produce the number of bytes its frame recorded —
    /// almost always because it was cut short in transit or in storage.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The same check also catches the opposite: a payload that decodes to <i>more</i> than was recorded,
    /// such as a framed gzip payload with a further gzip member concatenated onto it (gzip allows
    /// multi-member streams, and <see cref="System.IO.Compression.GZipStream"/> decodes every member).
    /// The error message says which direction the mismatch went.
    /// </para>
    /// <para>
    /// Only reachable for a <see cref="CompressionFraming.Framed"/> payload: nothing in a bare Brotli or
    /// gzip stream says how long the original was.
    /// </para>
    /// </remarks>
    public const string TruncatedPayload = "compression.truncated_payload";

    /// <summary>
    /// Decompressing the payload would exceed
    /// <see cref="Options.CompressionOptions.MaxDecompressedSize"/> — the defence against a
    /// decompression bomb, where a few hundred bytes expand to gigabytes.
    /// </summary>
    public const string PayloadTooLarge = "compression.payload_too_large";

    /// <summary>
    /// The payload is not a platform compression frame: it is shorter than a frame header, carries
    /// the wrong magic marker, or declares a format version this package does not understand.
    /// </summary>
    /// <remarks>
    /// The usual cause is reading bare algorithm bytes with a <see cref="CompressionFraming.Framed"/>
    /// compressor — resolve the matching <c>.Raw</c> keyed compressor instead.
    /// </remarks>
    public const string MalformedPayload = "compression.malformed_payload";

    /// <summary>
    /// The payload's frame records a different <see cref="CompressionAlgorithm"/> than the compressor
    /// that tried to read it — for example gzip bytes handed to the Brotli compressor.
    /// </summary>
    public const string AlgorithmMismatch = "compression.algorithm_mismatch";
}

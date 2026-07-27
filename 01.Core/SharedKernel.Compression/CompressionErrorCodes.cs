namespace SharedKernel.Compression;

/// <summary>
/// Well-known error code constants used by <c>SharedKernel.Compression</c> failure results.
/// </summary>
/// <remarks>
/// Defined locally per the platform convention that consuming packages may add their own
/// <c>ErrorCodes</c>-style constants without forking <c>SharedKernel.Primitives</c> — mirrors
/// <c>SharedKernel.Cryptography</c>'s <c>CryptographyErrorCodes</c>.
/// </remarks>
public static class CompressionErrorCodes
{
    /// <summary>
    /// The compressed payload could not be decompressed — it is corrupt, truncated, or was not
    /// produced by the algorithm the caller attempted to decompress it with.
    /// </summary>
    public const string DecompressionFailed = "compression.decompression_failed";
}

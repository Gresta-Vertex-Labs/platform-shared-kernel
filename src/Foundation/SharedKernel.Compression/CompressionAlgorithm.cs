namespace SharedKernel.Compression;

/// <summary>
/// The compression algorithm an <see cref="IPayloadCompressor"/> applies, and the value recorded in
/// a framed payload's header so the reader can tell which codec produced it.
/// </summary>
/// <remarks>
/// The numeric values are part of the framed payload format and are therefore permanent — an
/// already-written payload carries the value it was compressed with. Never renumber a member; a new
/// algorithm takes the next unused value.
/// </remarks>
public enum CompressionAlgorithm
{
    /// <summary>
    /// Brotli (<see cref="System.IO.Compression.BrotliStream"/>) — the platform default and the
    /// better ratio for the JSON/text-shaped payloads this platform mostly moves.
    /// </summary>
    Brotli = 1,

    /// <summary>
    /// gzip (<see cref="System.IO.Compression.GZipStream"/>) — the keyed alternate, for interop with
    /// systems that specifically require the gzip format.
    /// </summary>
    GZip = 2,
}

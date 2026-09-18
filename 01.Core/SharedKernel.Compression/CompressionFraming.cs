namespace SharedKernel.Compression;

/// <summary>
/// Whether a compressor wraps its output in the platform's compression frame, or emits the bare
/// algorithm bytes.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the difference between detecting a truncated payload and silently accepting one.</b>
/// Both <see cref="System.IO.Compression.BrotliStream"/> and
/// <see cref="System.IO.Compression.GZipStream"/> treat the end of the input as the end of the data,
/// so a payload cut short decompresses without error into a valid prefix of the original — nothing
/// downstream can tell it from complete data. The frame records the uncompressed length up front and
/// the reader verifies it, which turns that silent partial read into a failed
/// <see cref="SharedKernel.Primitives.Results.Result"/>.
/// </para>
/// <para>
/// Prefer <see cref="Framed"/> for anything this platform writes and reads back — a queue payload, a
/// stored blob, a cached value. Use <see cref="Raw"/> only where an external system dictates the
/// bytes on the wire.
/// </para>
/// </remarks>
public enum CompressionFraming
{
    /// <summary>
    /// Wrap the compressed bytes in the platform's frame: a magic marker, a format version, the
    /// algorithm, and the uncompressed length. Decompression rejects a payload that is not a frame,
    /// was produced by a different algorithm, or does not decompress to the recorded length.
    /// </summary>
    /// <remarks>
    /// Costs 13 bytes per payload. The frame is not a checksum and not authentication: it detects
    /// truncation and a wrong-codec read, not deliberate tampering by someone who can rewrite the
    /// header. Where the payload must be tamper-evident, encrypt it — AES-GCM's authentication tag
    /// covers the compressed bytes when you compress first and encrypt second.
    /// </remarks>
    Framed = 0,

    /// <summary>
    /// Emit only the bare algorithm bytes, so the output is an ordinary Brotli or gzip stream that
    /// any standard tool can read.
    /// </summary>
    /// <remarks>
    /// A truncated payload decompresses to a partial result and reports success, because nothing in
    /// either format marks where the data was supposed to end. The size cap
    /// (<see cref="Options.CompressionOptions.MaxDecompressedSize"/>) still applies. Choose this only
    /// for interop with a system outside this platform.
    /// </remarks>
    Raw = 1,
}

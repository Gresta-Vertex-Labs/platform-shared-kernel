using System.ComponentModel.DataAnnotations;
using System.IO.Compression;
using SharedKernel.Configuration;

namespace SharedKernel.Compression.Options;

/// <summary>
/// Configuration for the payload compressors registered by
/// <see cref="Extensions.CompressionServiceCollectionExtensions.AddSharedKernelCompression"/>.
/// </summary>
public sealed class CompressionOptions : ISectionBoundOptions
{
    /// <summary>The default for <see cref="MaxDecompressedSize"/>: 64 MiB.</summary>
    public const long DefaultMaxDecompressedSize = 64L * 1024 * 1024;

    /// <summary>The configuration section this options type binds from.</summary>
    public static string SectionName => "SharedKernel:Compression";

    /// <summary>
    /// The <see cref="System.IO.Compression.CompressionLevel"/> used when compressing. Defaults to
    /// <see cref="CompressionLevel.Optimal"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b><see cref="CompressionLevel.SmallestSize"/> is far more expensive on Brotli than the name
    /// suggests</b>, because it selects Brotli quality 11. Measured on 8 MiB of repetitive data:
    /// <c>Fastest</c> 2 ms, <c>Optimal</c> 10 ms, <c>SmallestSize</c> 225 ms — 22 times the cost of
    /// <c>Optimal</c> for a 1.3% smaller result. Do not set it on a request-path payload without
    /// measuring your own data; it belongs on archival writes, if anywhere.
    /// </para>
    /// <para>
    /// The level applies to both algorithms, so raising it for gzip interop also raises Brotli's
    /// cost. <see cref="CompressionLevel.NoCompression"/> still produces a valid, round-trippable
    /// stream in both formats.
    /// </para>
    /// </remarks>
    [EnumDataType(typeof(CompressionLevel))]
    public CompressionLevel Level { get; set; } = CompressionLevel.Optimal;

    /// <summary>
    /// The largest payload, in bytes, that decompression will produce before it gives up and returns
    /// <see cref="CompressionErrorCodes.PayloadTooLarge"/>. Defaults to
    /// <see cref="DefaultMaxDecompressedSize"/> (64 MiB).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is a denial-of-service control, not a tuning knob.</b> Compression ratios are
    /// unbounded in the attacker's favour: 102 bytes of Brotli expand to 64 MiB of zeroes, and
    /// crafted input goes orders of magnitude further. Without a cap, decompressing an untrusted
    /// payload — an inbound message body, a webhook delivery, a blob written by another system —
    /// allocates until the process dies.
    /// </para>
    /// <para>
    /// Enforced while decompressing rather than from the frame's recorded length, so a payload that
    /// understates its own size is still stopped. It bounds the bytes handed back to the caller; a
    /// <see cref="System.IO.Stream"/> destination may already hold up to the limit plus one buffer
    /// when the failure is reported. Raise it only for a path that genuinely moves payloads that
    /// large, and prefer raising it for that path's own compressor rather than globally.
    /// </para>
    /// </remarks>
    [Range(1, long.MaxValue)]
    public long MaxDecompressedSize { get; set; } = DefaultMaxDecompressedSize;
}

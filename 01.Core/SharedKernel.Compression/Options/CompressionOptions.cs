using System.ComponentModel.DataAnnotations;
using System.IO.Compression;

namespace SharedKernel.Compression.Options;

/// <summary>
/// Configuration for the payload compressors registered by
/// <see cref="Extensions.CompressionServiceCollectionExtensions.AddSharedKernelCompression"/>.
/// </summary>
public sealed class CompressionOptions
{
    /// <summary>The configuration section name this options class binds to.</summary>
    public const string SectionName = "SharedKernel:Compression";

    /// <summary>
    /// The <see cref="System.IO.Compression.CompressionLevel"/> used by both
    /// <see cref="BrotliPayloadCompressor"/> and <see cref="GZipPayloadCompressor"/> when
    /// compressing. Defaults to <see cref="CompressionLevel.Optimal"/> — the best balance of
    /// ratio and speed for most payloads.
    /// </summary>
    [EnumDataType(typeof(CompressionLevel))]
    public CompressionLevel Level { get; set; } = CompressionLevel.Optimal;
}

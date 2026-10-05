using System.IO.Compression;

namespace SharedKernel.Caching.FusionCache.Extensions;

/// <summary>
/// Settings for Brotli compression of distributed cache entries, passed to
/// <see cref="BrotliCompressionExtensions.AddBrotliCompression"/>.
/// </summary>
/// <remarks>Memory-cache entries are never compressed.</remarks>
public sealed class CacheCompressionOptions
{
    /// <summary>
    /// Gets or sets the smallest serialized entry, in bytes, that is compressed. Smaller entries are
    /// stored as they are. Defaults to 1024. Must be positive.
    /// </summary>
    public int ThresholdBytes { get; set; } = 1024;

    /// <summary>
    /// Gets or sets the compression level. Defaults to <see cref="CompressionLevel.Fastest"/>, which
    /// keeps the per-entry cost low on the read and write path.
    /// </summary>
    public CompressionLevel Level { get; set; } = CompressionLevel.Fastest;
}

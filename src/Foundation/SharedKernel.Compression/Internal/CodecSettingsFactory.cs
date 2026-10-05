using SharedKernel.Compression.Options;

namespace SharedKernel.Compression.Internal;

/// <summary>
/// Builds the <see cref="CodecSettings"/> a compressor runs under, validating the arguments both public
/// compressors share so neither can accept a value the other rejects.
/// </summary>
internal static class CodecSettingsFactory
{
    internal static CodecSettings Create(
        CompressionAlgorithm algorithm,
        CompressionFraming framing,
        CompressionOptions options)
    {
        // A cast from an out-of-range int reaches here as an undefined enum value; caught now rather
        // than silently treated as Framed deep inside the codec.
        if (framing is not (CompressionFraming.Framed or CompressionFraming.Raw))
            throw new ArgumentOutOfRangeException(nameof(framing), framing, "Not a defined CompressionFraming value.");

        // Options validation runs at host startup, but a directly constructed compressor never saw it.
        ArgumentOutOfRangeException.ThrowIfLessThan(options.MaxDecompressedSize, 1, nameof(options));

        return new CodecSettings(algorithm, framing, options.Level, options.MaxDecompressedSize);
    }
}

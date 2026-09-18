using System.IO.Compression;
using SharedKernel.Compression.Options;
using Xunit;

namespace SharedKernel.Compression.Tests;

public sealed class BrotliPayloadCompressorTests : PayloadCompressorContractTests
{
    protected override CompressionAlgorithm ExpectedAlgorithm => CompressionAlgorithm.Brotli;

    protected override IPayloadCompressor Create(
        CompressionLevel level = CompressionLevel.Optimal,
        CompressionFraming framing = CompressionFraming.Framed,
        long maxDecompressedSize = CompressionOptions.DefaultMaxDecompressedSize) =>
        new BrotliPayloadCompressor(OptionsFor(level, maxDecompressedSize), framing);

    protected override void CreateWithNullOptions() => _ = new BrotliPayloadCompressor(null!);

    [Fact]
    public void RawOutput_IsReadableByBrotliStreamDirectly()
    {
        // The whole point of raw mode: the bytes are an ordinary Brotli stream, no platform header.
        var original = SamplePayload();
        var compressed = Create(framing: CompressionFraming.Raw).Compress(original);

        using var input = new MemoryStream(compressed);
        using var brotli = new BrotliStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        brotli.CopyTo(output);

        Assert.Equal(original, output.ToArray());
    }
}

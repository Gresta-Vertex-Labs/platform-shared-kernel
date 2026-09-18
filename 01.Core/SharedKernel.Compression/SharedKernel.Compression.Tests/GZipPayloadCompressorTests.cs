using System.IO.Compression;
using SharedKernel.Compression.Options;
using Xunit;

namespace SharedKernel.Compression.Tests;

public sealed class GZipPayloadCompressorTests : PayloadCompressorContractTests
{
    protected override CompressionAlgorithm ExpectedAlgorithm => CompressionAlgorithm.GZip;

    protected override IPayloadCompressor Create(
        CompressionLevel level = CompressionLevel.Optimal,
        CompressionFraming framing = CompressionFraming.Framed,
        long maxDecompressedSize = CompressionOptions.DefaultMaxDecompressedSize) =>
        new GZipPayloadCompressor(OptionsFor(level, maxDecompressedSize), framing);

    protected override void CreateWithNullOptions() => _ = new GZipPayloadCompressor(null!);

    [Fact]
    public void RawOutput_IsAnOrdinaryGZipStream()
    {
        // Raw mode exists so an external system can read the bytes with any standard gzip tool.
        var original = SamplePayload();
        var compressed = Create(framing: CompressionFraming.Raw).Compress(original);

        Assert.Equal(0x1F, compressed[0]);
        Assert.Equal(0x8B, compressed[1]);

        using var input = new MemoryStream(compressed);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        gzip.CopyTo(output);

        Assert.Equal(original, output.ToArray());
    }

    [Fact]
    public void FramedOutput_IsNotAGZipStream()
    {
        // The counterpart warning: framed output is not a .gz body, so it is no use for interop.
        var compressed = Create().Compress(SamplePayload());

        Assert.NotEqual(0x1F, compressed[0]);
    }
}

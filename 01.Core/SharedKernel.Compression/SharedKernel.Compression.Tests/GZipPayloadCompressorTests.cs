using System.IO.Compression;
using Xunit;

namespace SharedKernel.Compression.Tests;

public sealed class GZipPayloadCompressorTests : PayloadCompressorContractTests
{
    protected override IPayloadCompressor CreateCompressor(CompressionLevel level = CompressionLevel.Optimal) =>
        new GZipPayloadCompressor(OptionsFor(level));

    protected override void CreateCompressorWithNullOptions() => _ = new GZipPayloadCompressor(null!);

    [Fact]
    public void Compress_ThenDecompress_ByteArray_RoundTrips_ForEmptyPayload()
    {
        var compressor = new GZipPayloadCompressor(OptionsFor(CompressionLevel.Optimal));

        byte[] compressed = compressor.Compress([]);
        var result = compressor.Decompress(compressed);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
    }

    [Fact]
    public void Decompress_ByteArray_PrefixTruncatedInput_ReturnsFailureResult()
    {
        // gzip's fixed 2-byte magic number at the start of every stream makes a prefix-truncated
        // (leading bytes missing) input reliably detectable — see the remarks on
        // PayloadCompressorContractTests.TruncatedHighEntropyPayload for why this is asserted
        // per-format rather than as a shared contract test.
        IPayloadCompressor compressor = CreateCompressor();
        byte[] truncated = TruncatedHighEntropyPayload(compressor);

        var result = compressor.Decompress(truncated);

        Assert.True(result.IsFailure);
        Assert.Equal(CompressionErrorCodes.DecompressionFailed, result.Error.Code);
    }
}

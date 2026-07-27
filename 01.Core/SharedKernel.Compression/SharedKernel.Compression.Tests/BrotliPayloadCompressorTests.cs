using System.IO.Compression;
using Xunit;

namespace SharedKernel.Compression.Tests;

public sealed class BrotliPayloadCompressorTests : PayloadCompressorContractTests
{
    protected override IPayloadCompressor CreateCompressor(CompressionLevel level = CompressionLevel.Optimal) =>
        new BrotliPayloadCompressor(OptionsFor(level));

    protected override void CreateCompressorWithNullOptions() => _ = new BrotliPayloadCompressor(null!);

    [Fact]
    public void Compress_ThenDecompress_ByteArray_RoundTrips_ForEmptyPayload()
    {
        var compressor = new BrotliPayloadCompressor(OptionsFor(CompressionLevel.Optimal));

        byte[] compressed = compressor.Compress([]);
        var result = compressor.Decompress(compressed);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
    }

    [Fact]
    public void Decompress_ByteArray_PrefixTruncatedInput_NeverThrows()
    {
        // Unlike gzip, Brotli's BCL implementation has no fixed magic-number gate at the start of
        // the stream, so a prefix-truncated input is not reliably rejected — it can legitimately
        // decode to a Result.Success carrying partial/garbage output instead of a Result failure.
        // See the remarks on PayloadCompressorContractTests.TruncatedHighEntropyPayload for the
        // full, empirically-verified explanation. The one guarantee this package makes regardless
        // is the one this test asserts: no exception ever propagates uncaught.
        IPayloadCompressor compressor = CreateCompressor();
        byte[] truncated = TruncatedHighEntropyPayload(compressor);

        Exception? exception = Record.Exception(() => compressor.Decompress(truncated));

        Assert.Null(exception);
    }
}

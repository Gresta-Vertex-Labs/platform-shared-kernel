using Microsoft.Extensions.Options;
using SharedKernel.Compression.Options;
using Xunit;

namespace SharedKernel.Compression.Tests;

/// <summary>
/// Regression tests for the decompression-bomb defect: before <see cref="CompressionOptions.MaxDecompressedSize"/>
/// existed, 102 bytes of Brotli expanded into an unbounded buffer, measured at 64 MiB in 162 ms — a
/// 658:1 ratio from trivially-constructed input, with crafted input going far further.
/// </summary>
public sealed class DecompressionLimitTests
{
    private const int PayloadSize = 4 * 1024 * 1024;

    private static IPayloadCompressor Create(
        CompressionAlgorithm algorithm,
        long maxDecompressedSize,
        CompressionFraming framing = CompressionFraming.Framed)
    {
        var options = Microsoft.Extensions.Options.Options.Create(new CompressionOptions { MaxDecompressedSize = maxDecompressedSize });
        return algorithm == CompressionAlgorithm.Brotli
            ? new BrotliPayloadCompressor(options, framing)
            : new GZipPayloadCompressor(options, framing);
    }

    /// <summary>A highly compressible payload — the shape a decompression bomb takes.</summary>
    private static byte[] Bomb(CompressionAlgorithm algorithm, CompressionFraming framing = CompressionFraming.Framed) =>
        Create(algorithm, CompressionOptions.DefaultMaxDecompressedSize, framing).Compress(new byte[PayloadSize]);

    [Theory]
    [InlineData(CompressionAlgorithm.Brotli)]
    [InlineData(CompressionAlgorithm.GZip)]
    public void Decompress_BeyondTheLimit_Fails(CompressionAlgorithm algorithm)
    {
        var bomb = Bomb(algorithm);
        Assert.True(bomb.Length < 64 * 1024, "the test payload should be tiny compressed");

        var result = Create(algorithm, maxDecompressedSize: 1024).Decompress(bomb);

        Assert.True(result.IsFailure);
        Assert.Equal(CompressionErrorCodes.PayloadTooLarge, result.Error.Code);
    }

    [Theory]
    [InlineData(CompressionAlgorithm.Brotli)]
    [InlineData(CompressionAlgorithm.GZip)]
    public void Decompress_BeyondTheLimit_Raw_Fails(CompressionAlgorithm algorithm)
    {
        // Raw mode cannot detect truncation, but the size cap still applies — it is enforced while
        // decompressing, not from the frame.
        var result = Create(algorithm, maxDecompressedSize: 1024, framing: CompressionFraming.Raw)
            .Decompress(Bomb(algorithm, CompressionFraming.Raw));

        Assert.True(result.IsFailure);
        Assert.Equal(CompressionErrorCodes.PayloadTooLarge, result.Error.Code);
    }

    [Theory]
    [InlineData(CompressionAlgorithm.Brotli)]
    [InlineData(CompressionAlgorithm.GZip)]
    public void Decompress_Stream_BeyondTheLimit_Fails_AndDoesNotWriteTheWholePayload(CompressionAlgorithm algorithm)
    {
        const long limit = 1024;
        using var input = new MemoryStream(Bomb(algorithm));
        using var output = new MemoryStream();

        var result = Create(algorithm, limit).Decompress(input, output);

        Assert.True(result.IsFailure);
        Assert.Equal(CompressionErrorCodes.PayloadTooLarge, result.Error.Code);

        // The documented bound: up to the limit plus one copy buffer may already have been written.
        Assert.True(output.Length < PayloadSize);
        Assert.True(output.Length <= limit + (81920 * 2));
    }

    [Theory]
    [InlineData(CompressionAlgorithm.Brotli)]
    [InlineData(CompressionAlgorithm.GZip)]
    public async Task DecompressAsync_BeyondTheLimit_Fails(CompressionAlgorithm algorithm)
    {
        using var input = new MemoryStream(Bomb(algorithm));
        using var output = new MemoryStream();

        var result = await Create(algorithm, maxDecompressedSize: 1024).DecompressAsync(input, output);

        Assert.True(result.IsFailure);
        Assert.Equal(CompressionErrorCodes.PayloadTooLarge, result.Error.Code);
    }

    [Theory]
    [InlineData(CompressionAlgorithm.Brotli)]
    [InlineData(CompressionAlgorithm.GZip)]
    public void Decompress_ExactlyAtTheLimit_Succeeds(CompressionAlgorithm algorithm)
    {
        // Off-by-one guard: the limit is inclusive.
        var compressor = Create(algorithm, maxDecompressedSize: PayloadSize);

        var result = compressor.Decompress(Bomb(algorithm));

        Assert.True(result.IsSuccess);
        Assert.Equal(PayloadSize, result.Value.Length);
    }

    [Theory]
    [InlineData(CompressionAlgorithm.Brotli)]
    [InlineData(CompressionAlgorithm.GZip)]
    public void Decompress_OneByteOverTheLimit_Fails(CompressionAlgorithm algorithm)
    {
        var result = Create(algorithm, maxDecompressedSize: PayloadSize - 1).Decompress(Bomb(algorithm));

        Assert.True(result.IsFailure);
        Assert.Equal(CompressionErrorCodes.PayloadTooLarge, result.Error.Code);
    }

    [Fact]
    public void DefaultLimit_Is64MiB()
    {
        Assert.Equal(64L * 1024 * 1024, new CompressionOptions().MaxDecompressedSize);
        Assert.Equal(CompressionOptions.DefaultMaxDecompressedSize, new CompressionOptions().MaxDecompressedSize);
    }

    [Theory]
    [InlineData(CompressionAlgorithm.Brotli)]
    [InlineData(CompressionAlgorithm.GZip)]
    public void Decompress_AFrameOverstatingItsLength_IsRejectedBeforeDecompressing(CompressionAlgorithm algorithm)
    {
        // A frame whose recorded length already exceeds the cap is refused up front, without doing
        // the work — the cheap path. The running counter still guards a frame that understates itself.
        var compressor = Create(algorithm, maxDecompressedSize: 1024);
        var compressed = Create(algorithm, CompressionOptions.DefaultMaxDecompressedSize).Compress(new byte[4096]);

        var result = compressor.Decompress(compressed);

        Assert.True(result.IsFailure);
        Assert.Equal(CompressionErrorCodes.PayloadTooLarge, result.Error.Code);
    }

    [Theory]
    [InlineData(CompressionAlgorithm.Brotli)]
    [InlineData(CompressionAlgorithm.GZip)]
    public void Decompress_AFrameUnderstatingItsLength_IsStillCapped(CompressionAlgorithm algorithm)
    {
        // Hand-forge a frame claiming 1 byte while carrying a 4 MiB payload: the cap must come from
        // the bytes actually produced, never from the payload's own claim about itself.
        var honest = Create(algorithm, CompressionOptions.DefaultMaxDecompressedSize).Compress(new byte[PayloadSize]);
        var lying = honest.ToArray();
        System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(lying.AsSpan(5), 1);

        var result = Create(algorithm, maxDecompressedSize: 1024).Decompress(lying);

        Assert.True(result.IsFailure);
        Assert.Equal(CompressionErrorCodes.PayloadTooLarge, result.Error.Code);
    }
}

using System.IO.Compression;
using System.Text;
using Microsoft.Extensions.Options;
using SharedKernel.Compression.Options;
using SharedKernel.Primitives.Results;
using Xunit;

namespace SharedKernel.Compression.Tests;

/// <summary>
/// Shared contract tests run against every <see cref="IPayloadCompressor"/> implementation.
/// Concrete subclasses supply only the factory and the algorithm they expect — every test here then
/// exercises the full public contract identically for <see cref="BrotliPayloadCompressor"/> and
/// <see cref="GZipPayloadCompressor"/>.
/// </summary>
public abstract class PayloadCompressorContractTests
{
    protected abstract CompressionAlgorithm ExpectedAlgorithm { get; }

    protected abstract IPayloadCompressor Create(
        CompressionLevel level = CompressionLevel.Optimal,
        CompressionFraming framing = CompressionFraming.Framed,
        long maxDecompressedSize = CompressionOptions.DefaultMaxDecompressedSize);

    protected abstract void CreateWithNullOptions();

    /// <summary>
    /// A payload big enough that cutting it anywhere lands inside real entropy-coded data, and
    /// redundant enough to compress well — roughly 283 KB of JSON-shaped text.
    /// </summary>
    protected static byte[] LargePayload()
    {
        var json = "[" + string.Join(",", Enumerable.Range(0, 4000)
            .Select(i => $"{{\"id\":{i},\"name\":\"customer-{i}\",\"balance\":{i * 37}.50,\"currency\":\"TRY\"}}")) + "]";
        return Encoding.UTF8.GetBytes(json);
    }

    protected static byte[] SamplePayload(string text =
        "The quick brown fox jumps over the lazy dog. Repeated text compresses well: " +
        "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.") =>
        Encoding.UTF8.GetBytes(text);

    protected static IOptions<CompressionOptions> OptionsFor(
        CompressionLevel level,
        long maxDecompressedSize = CompressionOptions.DefaultMaxDecompressedSize) =>
        Microsoft.Extensions.Options.Options.Create(
            new CompressionOptions { Level = level, MaxDecompressedSize = maxDecompressedSize });

    [Fact]
    public void Algorithm_ReportsTheAlgorithmItApplies()
    {
        Assert.Equal(ExpectedAlgorithm, Create().Algorithm);
    }

    [Theory]
    [InlineData(CompressionFraming.Framed)]
    [InlineData(CompressionFraming.Raw)]
    public void Framing_ReportsWhatItWasConstructedWith(CompressionFraming framing)
    {
        Assert.Equal(framing, Create(framing: framing).Framing);
    }

    [Theory]
    [InlineData(CompressionFraming.Framed)]
    [InlineData(CompressionFraming.Raw)]
    public void Compress_ThenDecompress_ByteArray_RoundTrips(CompressionFraming framing)
    {
        var compressor = Create(framing: framing);
        var original = LargePayload();

        var compressed = compressor.Compress(original);
        var result = compressor.Decompress(compressed);

        Assert.True(result.IsSuccess);
        Assert.Equal(original, result.Value);
    }

    [Theory]
    [InlineData(CompressionFraming.Framed)]
    [InlineData(CompressionFraming.Raw)]
    public void Compress_ThenDecompress_EmptyPayload_RoundTrips(CompressionFraming framing)
    {
        var compressor = Create(framing: framing);

        var result = compressor.Decompress(compressor.Compress([]));

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
    }

    [Fact]
    public void Compress_ProducesSmallerOutput_ForRedundantInput()
    {
        var original = LargePayload();

        Assert.True(Create().Compress(original).Length < original.Length);
    }

    [Theory]
    [InlineData(CompressionFraming.Framed)]
    [InlineData(CompressionFraming.Raw)]
    public void Compress_ThenDecompress_Stream_RoundTrips(CompressionFraming framing)
    {
        var compressor = Create(framing: framing);
        var original = LargePayload();

        using var compressed = new MemoryStream();
        using (var input = new MemoryStream(original))
        {
            compressor.Compress(input, compressed);
        }

        compressed.Position = 0;
        using var decompressed = new MemoryStream();
        var result = compressor.Decompress(compressed, decompressed);

        Assert.True(result.IsSuccess);
        Assert.Equal(original, decompressed.ToArray());
    }

    [Theory]
    [InlineData(CompressionFraming.Framed)]
    [InlineData(CompressionFraming.Raw)]
    public async Task CompressAsync_ThenDecompressAsync_Stream_RoundTrips(CompressionFraming framing)
    {
        var compressor = Create(framing: framing);
        var original = LargePayload();

        using var compressed = new MemoryStream();
        using (var input = new MemoryStream(original))
        {
            await compressor.CompressAsync(input, compressed);
        }

        compressed.Position = 0;
        using var decompressed = new MemoryStream();
        var result = await compressor.DecompressAsync(compressed, decompressed);

        Assert.True(result.IsSuccess);
        Assert.Equal(original, decompressed.ToArray());
    }

    [Fact]
    public void ByteArrayAndStreamOverloads_ProduceIdenticalOutput()
    {
        var compressor = Create();
        var compressed = compressor.Compress(LargePayload());

        var fromBytes = compressor.Decompress(compressed);

        using var input = new MemoryStream(compressed);
        using var output = new MemoryStream();
        var fromStream = compressor.Decompress(input, output);

        Assert.True(fromBytes.IsSuccess);
        Assert.True(fromStream.IsSuccess);
        Assert.Equal(fromBytes.Value, output.ToArray());
    }

    [Fact]
    public void Compress_StreamOverload_ProducesAPayloadTheByteArrayOverloadCanRead()
    {
        var compressor = Create();
        var original = LargePayload();

        using var compressed = new MemoryStream();
        using (var input = new MemoryStream(original))
        {
            compressor.Compress(input, compressed);
        }

        var result = compressor.Decompress(compressed.ToArray());

        Assert.True(result.IsSuccess);
        Assert.Equal(original, result.Value);
    }

    [Theory]
    [InlineData(CompressionFraming.Framed)]
    [InlineData(CompressionFraming.Raw)]
    public void Decompress_CorruptedInput_Fails(CompressionFraming framing)
    {
        var compressor = Create(framing: framing);
        var corrupted = compressor.Compress(LargePayload());
        corrupted[corrupted.Length / 2] ^= 0xFF;

        var result = compressor.Decompress(corrupted);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void Decompress_CorruptedInput_Stream_Fails()
    {
        var compressor = Create();
        var corrupted = compressor.Compress(LargePayload());
        corrupted[corrupted.Length / 2] ^= 0xFF;

        using var input = new MemoryStream(corrupted);
        using var output = new MemoryStream();

        Assert.True(compressor.Decompress(input, output).IsFailure);
    }

    [Fact]
    public async Task DecompressAsync_CorruptedInput_Stream_Fails()
    {
        var compressor = Create();
        var corrupted = compressor.Compress(LargePayload());
        corrupted[corrupted.Length / 2] ^= 0xFF;

        using var input = new MemoryStream(corrupted);
        using var output = new MemoryStream();

        Assert.True((await compressor.DecompressAsync(input, output)).IsFailure);
    }

    [Theory]
    [InlineData(CompressionFraming.Framed)]
    [InlineData(CompressionFraming.Raw)]
    public void Decompress_Garbage_FailsAndNeverThrows(CompressionFraming framing)
    {
        var compressor = Create(framing: framing);
        var garbage = Encoding.UTF8.GetBytes("this is definitely not a compressed payload");

        Result<byte[]> result = default!;
        Assert.Null(Record.Exception(() => result = compressor.Decompress(garbage)));
        Assert.True(result.IsFailure);
    }

    [Fact]
    public void Decompress_EveryFailure_IsAValidationError()
    {
        // The fault is in the supplied payload, never in the service — a 400 at the boundary, not a 500.
        var compressor = Create();

        var result = compressor.Decompress(Encoding.UTF8.GetBytes("not a payload at all"));

        Assert.True(result.IsFailure);
        Assert.Equal(Primitives.Errors.ErrorType.Validation, result.Error.Type);
    }

    [Fact]
    public void Compress_NullByteArray_Throws() =>
        Assert.Throws<ArgumentNullException>(() => Create().Compress((byte[])null!));

    [Fact]
    public void Compress_NullInputStream_Throws()
    {
        using var output = new MemoryStream();
        Assert.Throws<ArgumentNullException>(() => Create().Compress(null!, output));
    }

    [Fact]
    public void Compress_NullOutputStream_Throws()
    {
        using var input = new MemoryStream();
        Assert.Throws<ArgumentNullException>(() => Create().Compress(input, null!));
    }

    [Fact]
    public void Decompress_NullByteArray_Throws() =>
        Assert.Throws<ArgumentNullException>(() => Create().Decompress((byte[])null!));

    [Fact]
    public void Decompress_NullInputStream_Throws()
    {
        using var output = new MemoryStream();
        Assert.Throws<ArgumentNullException>(() => Create().Decompress(null!, output));
    }

    [Fact]
    public void Decompress_NullOutputStream_Throws()
    {
        using var input = new MemoryStream();
        Assert.Throws<ArgumentNullException>(() => Create().Decompress(input, null!));
    }

    [Fact]
    public async Task CompressAsync_RespectsCancellation()
    {
        var compressor = Create();
        using var input = new MemoryStream(SamplePayload());
        using var output = new MemoryStream();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await compressor.CompressAsync(input, output, cts.Token));
    }

    [Fact]
    public async Task DecompressAsync_RespectsCancellation()
    {
        var compressor = Create();
        var compressed = compressor.Compress(SamplePayload());
        using var input = new MemoryStream(compressed);
        using var output = new MemoryStream();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await compressor.DecompressAsync(input, output, cts.Token));
    }

    [Fact]
    public void Constructor_NullOptions_Throws() => Assert.Throws<ArgumentNullException>(CreateWithNullOptions);

    [Fact]
    public void Constructor_UndefinedFraming_Throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Create(framing: (CompressionFraming)99));

    [Fact]
    public void Constructor_NonPositiveMaxDecompressedSize_Throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Create(maxDecompressedSize: 0));

    [Theory]
    [InlineData(CompressionLevel.NoCompression)]
    [InlineData(CompressionLevel.Fastest)]
    [InlineData(CompressionLevel.Optimal)]
    [InlineData(CompressionLevel.SmallestSize)]
    public void EveryCompressionLevel_RoundTrips(CompressionLevel level)
    {
        var compressor = Create(level);
        var original = SamplePayload();

        var result = compressor.Decompress(compressor.Compress(original));

        Assert.True(result.IsSuccess);
        Assert.Equal(original, result.Value);
    }
}

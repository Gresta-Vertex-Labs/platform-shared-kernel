using System.IO.Compression;
using System.Text;
using Microsoft.Extensions.Options;
using SharedKernel.Compression.Options;
using SharedKernel.Primitives.Results;
using Xunit;

namespace SharedKernel.Compression.Tests;

/// <summary>
/// Shared contract tests run against every <see cref="IPayloadCompressor"/> implementation.
/// Concrete subclasses supply only <see cref="CreateCompressor"/> — every test here then exercises
/// the full public contract (byte[] and stream overloads, sync and async, corrupt/truncated-input
/// failure handling) identically for <see cref="BrotliPayloadCompressor"/> and
/// <see cref="GZipPayloadCompressor"/>.
/// </summary>
public abstract class PayloadCompressorContractTests
{
    protected abstract IPayloadCompressor CreateCompressor(CompressionLevel level = CompressionLevel.Optimal);

    private static byte[] SamplePayload(string text =
        "The quick brown fox jumps over the lazy dog. Repeated text compresses well: " +
        "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.") =>
        Encoding.UTF8.GetBytes(text);

    /// <summary>
    /// A large, near-incompressible payload (fixed-seed pseudo-random bytes) used only for the
    /// truncation test. <see cref="SamplePayload"/>'s highly redundant text compresses down so far
    /// that a naive "cut it in half" truncation can land entirely after the real entropy-coded
    /// content and only remove trailer bytes gzip's BCL implementation does not validate on read —
    /// producing a silent, non-erroring "success" that is a genuine, confirmed BCL characteristic
    /// (not a bug in this package) rather than a reliable test of truncation detection. Random,
    /// (near-)incompressible content keeps compressed output close to the same size as the input,
    /// so any mid-stream cut reliably lands inside genuine entropy-coded data for both algorithms.
    /// </summary>
    private static byte[] HighEntropyPayload()
    {
        var random = new Random(Seed: 20260727);
        byte[] buffer = new byte[4096];
        random.NextBytes(buffer);
        return buffer;
    }

    [Fact]
    public void Compress_ThenDecompress_ByteArray_RoundTrips()
    {
        IPayloadCompressor compressor = CreateCompressor();
        byte[] original = SamplePayload();

        byte[] compressed = compressor.Compress(original);
        Result<byte[]> result = compressor.Decompress(compressed);

        Assert.True(result.IsSuccess);
        Assert.Equal(original, result.Value);
    }

    [Fact]
    public void Compress_ProducesSmallerOutput_ForRedundantInput()
    {
        IPayloadCompressor compressor = CreateCompressor();
        byte[] original = SamplePayload();

        byte[] compressed = compressor.Compress(original);

        Assert.True(compressed.Length < original.Length);
    }

    [Fact]
    public void Compress_ThenDecompress_Stream_RoundTrips()
    {
        IPayloadCompressor compressor = CreateCompressor();
        byte[] original = SamplePayload();

        using var compressedStream = new MemoryStream();
        using (var inputStream = new MemoryStream(original))
        {
            compressor.Compress(inputStream, compressedStream);
        }

        compressedStream.Position = 0;
        using var decompressedStream = new MemoryStream();
        Result result = compressor.Decompress(compressedStream, decompressedStream);

        Assert.True(result.IsSuccess);
        Assert.Equal(original, decompressedStream.ToArray());
    }

    [Fact]
    public async Task CompressAsync_ThenDecompressAsync_Stream_RoundTrips()
    {
        IPayloadCompressor compressor = CreateCompressor();
        byte[] original = SamplePayload();

        using var compressedStream = new MemoryStream();
        using (var inputStream = new MemoryStream(original))
        {
            await compressor.CompressAsync(inputStream, compressedStream);
        }

        compressedStream.Position = 0;
        using var decompressedStream = new MemoryStream();
        Result result = await compressor.DecompressAsync(compressedStream, decompressedStream);

        Assert.True(result.IsSuccess);
        Assert.Equal(original, decompressedStream.ToArray());
    }

    [Fact]
    public void StreamingAndInMemoryOverloads_ProduceByteIdenticalDecompressedOutput()
    {
        IPayloadCompressor compressor = CreateCompressor();
        byte[] original = SamplePayload();
        byte[] compressed = compressor.Compress(original);

        // Decompress via the byte[] overload.
        Result<byte[]> fromBytes = compressor.Decompress(compressed);

        // Decompress the same compressed payload via the streaming overload.
        using var input = new MemoryStream(compressed);
        using var output = new MemoryStream();
        Result fromStream = compressor.Decompress(input, output);

        Assert.True(fromBytes.IsSuccess);
        Assert.True(fromStream.IsSuccess);
        Assert.Equal(fromBytes.Value, output.ToArray());
    }

    [Fact]
    public void Decompress_ByteArray_CorruptedInput_ReturnsFailureResult()
    {
        IPayloadCompressor compressor = CreateCompressor();
        byte[] compressed = compressor.Compress(SamplePayload());

        // Flip a byte in the middle of the compressed payload to corrupt it.
        byte[] corrupted = (byte[])compressed.Clone();
        int middle = corrupted.Length / 2;
        corrupted[middle] ^= 0xFF;

        Result<byte[]> result = compressor.Decompress(corrupted);

        Assert.True(result.IsFailure);
        Assert.Equal(CompressionErrorCodes.DecompressionFailed, result.Error.Code);
    }

    /// <summary>
    /// Truncated-input handling is intentionally <b>not</b> a shared contract test.
    /// </summary>
    /// <remarks>
    /// Empirically verified against this BCL version: <see cref="System.IO.Compression.GZipStream"/>
    /// exposes a fixed 2-byte magic number at the start of every stream, so a prefix-truncated
    /// (leading bytes missing) input is reliably rejected. <see cref="System.IO.Compression.BrotliStream"/>
    /// has no equivalent fixed magic-number gate — its decoder can, depending on the exact byte
    /// content, decode a truncated/prefix-truncated input without raising any error at all, silently
    /// producing whatever partial (possibly empty) output it managed to derive. Neither algorithm's
    /// BCL implementation validates that the originally-compressed length was fully reproduced, and
    /// gzip's own trailing CRC32/ISIZE footer is likewise not validated on read. This is a genuine,
    /// confirmed platform characteristic — not a defect in this package — so each subclass asserts
    /// only the guarantee its underlying algorithm actually provides; see
    /// <see cref="GZipPayloadCompressorTests"/> and <see cref="BrotliPayloadCompressorTests"/>.
    /// Callers who must detect a truncated upload/transfer end-to-end should pair compression with
    /// a separate integrity check (e.g. <c>IContentHasher</c> or a known expected length), not rely
    /// solely on the compression format's own error signaling.
    /// </remarks>
    protected static byte[] TruncatedHighEntropyPayload(IPayloadCompressor compressor)
    {
        byte[] compressed = compressor.Compress(HighEntropyPayload());
        return compressed[(compressed.Length / 2)..];
    }

    [Fact]
    public void Decompress_Stream_CorruptedInput_ReturnsFailureResult()
    {
        IPayloadCompressor compressor = CreateCompressor();
        byte[] compressed = compressor.Compress(SamplePayload());
        byte[] corrupted = (byte[])compressed.Clone();
        corrupted[corrupted.Length / 2] ^= 0xFF;

        using var input = new MemoryStream(corrupted);
        using var output = new MemoryStream();
        Result result = compressor.Decompress(input, output);

        Assert.True(result.IsFailure);
        Assert.Equal(CompressionErrorCodes.DecompressionFailed, result.Error.Code);
    }

    [Fact]
    public async Task DecompressAsync_Stream_CorruptedInput_ReturnsFailureResult()
    {
        IPayloadCompressor compressor = CreateCompressor();
        byte[] compressed = compressor.Compress(SamplePayload());
        byte[] corrupted = (byte[])compressed.Clone();
        corrupted[corrupted.Length / 2] ^= 0xFF;

        using var input = new MemoryStream(corrupted);
        using var output = new MemoryStream();
        Result result = await compressor.DecompressAsync(input, output);

        Assert.True(result.IsFailure);
        Assert.Equal(CompressionErrorCodes.DecompressionFailed, result.Error.Code);
    }

    [Fact]
    public void Decompress_GarbageInput_ReturnsFailureResult_NeverThrows()
    {
        IPayloadCompressor compressor = CreateCompressor();
        byte[] garbage = Encoding.UTF8.GetBytes("this is definitely not a compressed payload");

        Result<byte[]> result = compressor.Decompress(garbage);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void Compress_NullByteArray_Throws()
    {
        IPayloadCompressor compressor = CreateCompressor();

        Assert.Throws<ArgumentNullException>(() => compressor.Compress((byte[])null!));
    }

    [Fact]
    public void Compress_NullInputStream_Throws()
    {
        IPayloadCompressor compressor = CreateCompressor();
        using var output = new MemoryStream();

        Assert.Throws<ArgumentNullException>(() => compressor.Compress(null!, output));
    }

    [Fact]
    public void Compress_NullOutputStream_Throws()
    {
        IPayloadCompressor compressor = CreateCompressor();
        using var input = new MemoryStream();

        Assert.Throws<ArgumentNullException>(() => compressor.Compress(input, null!));
    }

    [Fact]
    public void Decompress_NullByteArray_Throws()
    {
        IPayloadCompressor compressor = CreateCompressor();

        Assert.Throws<ArgumentNullException>(() => compressor.Decompress((byte[])null!));
    }

    [Fact]
    public void Decompress_NullInputStream_Throws()
    {
        IPayloadCompressor compressor = CreateCompressor();
        using var output = new MemoryStream();

        Assert.Throws<ArgumentNullException>(() => compressor.Decompress(null!, output));
    }

    [Fact]
    public void Decompress_NullOutputStream_Throws()
    {
        IPayloadCompressor compressor = CreateCompressor();
        using var input = new MemoryStream();

        Assert.Throws<ArgumentNullException>(() => compressor.Decompress(input, null!));
    }

    [Fact]
    public async Task CompressAsync_RespectsCancellation()
    {
        IPayloadCompressor compressor = CreateCompressor();
        using var input = new MemoryStream(SamplePayload());
        using var output = new MemoryStream();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => compressor.CompressAsync(input, output, cts.Token));
    }

    [Fact]
    public async Task DecompressAsync_RespectsCancellation()
    {
        IPayloadCompressor compressor = CreateCompressor();
        byte[] compressed = compressor.Compress(SamplePayload());
        using var input = new MemoryStream(compressed);
        using var output = new MemoryStream();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => compressor.DecompressAsync(input, output, cts.Token));
    }

    [Fact]
    public void Constructor_NullOptions_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => CreateCompressorWithNullOptions());
    }

    protected abstract void CreateCompressorWithNullOptions();

    protected static IOptions<CompressionOptions> OptionsFor(CompressionLevel level) =>
        Microsoft.Extensions.Options.Options.Create(new CompressionOptions { Level = level });
}

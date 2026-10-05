using System.Text;
using Microsoft.Extensions.Options;
using SharedKernel.Compression.Options;
using Xunit;

namespace SharedKernel.Compression.Tests;

/// <summary>
/// Regression tests for the defect the frame exists to fix: before it, a truncated payload
/// decompressed to a <i>valid prefix</i> of the original and reported success, so nothing downstream
/// could tell it from complete data. Measured on this payload, 50% of the compressed bytes yielded
/// 127,863 of 282,775 bytes with <c>IsSuccess == true</c>.
/// </summary>
public sealed class TruncationDetectionTests
{
    private static byte[] Payload()
    {
        var json = "[" + string.Join(",", Enumerable.Range(0, 4000)
            .Select(i => $"{{\"id\":{i},\"name\":\"customer-{i}\",\"balance\":{i * 37}.50,\"currency\":\"TRY\"}}")) + "]";
        return Encoding.UTF8.GetBytes(json);
    }

    private static IPayloadCompressor Create(
        CompressionAlgorithm algorithm,
        CompressionFraming framing = CompressionFraming.Framed)
    {
        var options = Microsoft.Extensions.Options.Options.Create(new CompressionOptions());
        return algorithm == CompressionAlgorithm.Brotli
            ? new BrotliPayloadCompressor(options, framing)
            : new GZipPayloadCompressor(options, framing);
    }

    [Theory]
    [InlineData(CompressionAlgorithm.Brotli, 25)]
    [InlineData(CompressionAlgorithm.Brotli, 50)]
    [InlineData(CompressionAlgorithm.Brotli, 75)]
    [InlineData(CompressionAlgorithm.Brotli, 99)]
    [InlineData(CompressionAlgorithm.GZip, 25)]
    [InlineData(CompressionAlgorithm.GZip, 50)]
    [InlineData(CompressionAlgorithm.GZip, 75)]
    [InlineData(CompressionAlgorithm.GZip, 99)]
    public void Framed_TruncatedPayload_Fails(CompressionAlgorithm algorithm, int percentKept)
    {
        var compressor = Create(algorithm);
        var compressed = compressor.Compress(Payload());
        var truncated = compressed[..(compressed.Length * percentKept / 100)];

        var result = compressor.Decompress(truncated);

        Assert.True(result.IsFailure);
        Assert.Equal(CompressionErrorCodes.TruncatedPayload, result.Error.Code);
    }

    [Theory]
    [InlineData(CompressionAlgorithm.Brotli)]
    [InlineData(CompressionAlgorithm.GZip)]
    public void Framed_TruncatedPayload_Stream_Fails(CompressionAlgorithm algorithm)
    {
        var compressor = Create(algorithm);
        var compressed = compressor.Compress(Payload());

        using var input = new MemoryStream(compressed[..(compressed.Length / 2)]);
        using var output = new MemoryStream();
        var result = compressor.Decompress(input, output);

        Assert.True(result.IsFailure);
        Assert.Equal(CompressionErrorCodes.TruncatedPayload, result.Error.Code);
    }

    [Theory]
    [InlineData(CompressionAlgorithm.Brotli)]
    [InlineData(CompressionAlgorithm.GZip)]
    public async Task Framed_TruncatedPayload_Async_Fails(CompressionAlgorithm algorithm)
    {
        var compressor = Create(algorithm);
        var compressed = compressor.Compress(Payload());

        using var input = new MemoryStream(compressed[..(compressed.Length / 2)]);
        using var output = new MemoryStream();
        var result = await compressor.DecompressAsync(input, output);

        Assert.True(result.IsFailure);
        Assert.Equal(CompressionErrorCodes.TruncatedPayload, result.Error.Code);
    }

    [Theory]
    [InlineData(CompressionAlgorithm.Brotli)]
    [InlineData(CompressionAlgorithm.GZip)]
    public void Framed_TruncatedToLessThanAHeader_ReportsMalformed(CompressionAlgorithm algorithm)
    {
        var compressor = Create(algorithm);
        var compressed = compressor.Compress(Payload());

        var result = compressor.Decompress(compressed[..5]);

        Assert.True(result.IsFailure);
        Assert.Equal(CompressionErrorCodes.MalformedPayload, result.Error.Code);
    }

    [Theory]
    [InlineData(CompressionAlgorithm.Brotli)]
    [InlineData(CompressionAlgorithm.GZip)]
    public void Raw_TruncatedPayload_StillSilentlySucceeds_AsDocumented(CompressionAlgorithm algorithm)
    {
        // Pins the documented cost of raw mode rather than pretending it is safe: nothing in a bare
        // Brotli or gzip stream says how long the original was, so truncation cannot be detected.
        // This is why Framed is the default and raw is for external interop only.
        var compressor = Create(algorithm, CompressionFraming.Raw);
        var payload = Payload();
        var compressed = compressor.Compress(payload);

        var result = compressor.Decompress(compressed[..(compressed.Length / 2)]);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.Length < payload.Length);
        Assert.Equal(payload.AsSpan(0, result.Value.Length).ToArray(), result.Value);
    }

    [Theory]
    [InlineData(CompressionAlgorithm.Brotli, CompressionFraming.Framed)]
    [InlineData(CompressionAlgorithm.GZip, CompressionFraming.Framed)]
    [InlineData(CompressionAlgorithm.Brotli, CompressionFraming.Raw)]
    [InlineData(CompressionAlgorithm.GZip, CompressionFraming.Raw)]
    public void JunkBytesAppendedAfterACompletePayload_AreIgnored(
        CompressionAlgorithm algorithm,
        CompressionFraming framing)
    {
        // Measured: both decompressors stop at the end of their own data, so bytes that are not another
        // compressed stream are ignored and the payload still decodes byte-for-byte.
        var compressor = Create(algorithm, framing);
        var payload = Payload();
        var compressed = compressor.Compress(payload);

        var result = compressor.Decompress(compressed.Concat(new byte[] { 1, 2, 3, 4, 5 }).ToArray());

        Assert.True(result.IsSuccess);
        Assert.Equal(payload, result.Value);
    }

    [Theory]
    [InlineData(CompressionAlgorithm.Brotli, CompressionFraming.Framed)]
    [InlineData(CompressionAlgorithm.GZip, CompressionFraming.Framed)]
    [InlineData(CompressionAlgorithm.Brotli, CompressionFraming.Raw)]
    public void ASecondPayloadAppended_IsIgnored(CompressionAlgorithm algorithm, CompressionFraming framing)
    {
        // Brotli has no multi-stream format, and a second framed payload starts with the "SKC" header,
        // which is not a gzip member — so in these three modes the second payload is ignored.
        var compressor = Create(algorithm, framing);
        var payload = Payload();
        var compressed = compressor.Compress(payload);

        var result = compressor.Decompress(compressed.Concat(compressed).ToArray());

        Assert.True(result.IsSuccess);
        Assert.Equal(payload, result.Value);
    }

    [Fact]
    public void RawGZip_DecodesAConcatenatedMember_AsGZipAllows()
    {
        // RFC 1952 makes a sequence of gzip members one valid gzip file (`cat a.gz b.gz` is legal), and
        // GZipStream decodes every member. Measured: 23,889 bytes in, 47,778 out. Correct gzip behaviour,
        // and exactly why raw mode is for interop only — nothing records how long the original was.
        var compressor = Create(CompressionAlgorithm.GZip, CompressionFraming.Raw);
        var payload = Payload();
        var compressed = compressor.Compress(payload);

        var result = compressor.Decompress(compressed.Concat(compressed).ToArray());

        Assert.True(result.IsSuccess);
        Assert.Equal(payload.Concat(payload).ToArray(), result.Value);
    }

    [Fact]
    public void FramedGZip_ExtendedByAnAppendedMember_Fails()
    {
        // The same concatenation against a framed payload: the extra member decodes, the output no longer
        // matches the recorded length, and the frame rejects it. The length check guards extension as
        // well as truncation.
        var framed = Create(CompressionAlgorithm.GZip);
        var raw = Create(CompressionAlgorithm.GZip, CompressionFraming.Raw);
        var payload = Payload();

        var extended = framed.Compress(payload).Concat(raw.Compress(payload)).ToArray();
        var result = framed.Decompress(extended);

        Assert.True(result.IsFailure);
        Assert.Equal(CompressionErrorCodes.TruncatedPayload, result.Error.Code);
    }

    [Theory]
    [InlineData(CompressionAlgorithm.Brotli)]
    [InlineData(CompressionAlgorithm.GZip)]
    public void EveryDecompressOverload_AgreesOnATruncatedPayload(CompressionAlgorithm algorithm)
    {
        // The byte[] overload reads the caller's array in place while the span overload copies it into a
        // pooled buffer first — two code paths that must reach the same verdict.
        var compressor = Create(algorithm);
        var compressed = compressor.Compress(Payload());
        var truncated = compressed[..(compressed.Length / 2)];

        var fromArray = compressor.Decompress(truncated);
        var fromSpan = compressor.Decompress(truncated.AsSpan());
        var toWriter = compressor.Decompress(truncated.AsSpan(), new System.Buffers.ArrayBufferWriter<byte>());

        Assert.Equal(CompressionErrorCodes.TruncatedPayload, fromArray.Error.Code);
        Assert.Equal(CompressionErrorCodes.TruncatedPayload, fromSpan.Error.Code);
        Assert.Equal(CompressionErrorCodes.TruncatedPayload, toWriter.Error.Code);
    }
}

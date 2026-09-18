using System.Buffers;
using System.Buffers.Binary;
using System.Text;
using Microsoft.Extensions.Options;
using SharedKernel.Compression.Options;
using Xunit;

namespace SharedKernel.Compression.Tests;

/// <summary>
/// What a compressor will and will not read: the frame carries the algorithm, so every mismatched
/// pairing fails with a distinct code instead of returning wrong bytes.
/// </summary>
public sealed class FrameCompatibilityTests
{
    private const int HeaderSize = 13;

    private static byte[] Payload() => Encoding.UTF8.GetBytes(new string('A', 5000) + "-END");

    private static IPayloadCompressor Brotli(CompressionFraming framing = CompressionFraming.Framed) =>
        new BrotliPayloadCompressor(Microsoft.Extensions.Options.Options.Create(new CompressionOptions()), framing);

    private static IPayloadCompressor GZip(CompressionFraming framing = CompressionFraming.Framed) =>
        new GZipPayloadCompressor(Microsoft.Extensions.Options.Options.Create(new CompressionOptions()), framing);

    [Fact]
    public void FramedBrotliPayload_ReadByGZip_ReportsAlgorithmMismatch()
    {
        var result = GZip().Decompress(Brotli().Compress(Payload()));

        Assert.True(result.IsFailure);
        Assert.Equal(CompressionErrorCodes.AlgorithmMismatch, result.Error.Code);
    }

    [Fact]
    public void FramedGZipPayload_ReadByBrotli_ReportsAlgorithmMismatch()
    {
        // Brotli has no magic number of its own, so without the frame this is the pairing most at
        // risk of decoding to garbage rather than failing.
        var result = Brotli().Decompress(GZip().Compress(Payload()));

        Assert.True(result.IsFailure);
        Assert.Equal(CompressionErrorCodes.AlgorithmMismatch, result.Error.Code);
    }

    [Fact]
    public void RawPayload_ReadByAFramedCompressor_ReportsMalformed()
    {
        var result = Brotli().Decompress(Brotli(CompressionFraming.Raw).Compress(Payload()));

        Assert.True(result.IsFailure);
        Assert.Equal(CompressionErrorCodes.MalformedPayload, result.Error.Code);
    }

    [Fact]
    public void FramedPayload_ReadByARawCompressor_Fails()
    {
        // The header is not valid compressed data, so the decoder rejects it.
        Assert.True(Brotli(CompressionFraming.Raw).Decompress(Brotli().Compress(Payload())).IsFailure);
    }

    [Fact]
    public void FramedPayload_CarriesAThirteenByteHeader()
    {
        var payload = Payload();
        var framed = Brotli().Compress(payload);
        var raw = Brotli(CompressionFraming.Raw).Compress(payload);

        Assert.Equal(raw.Length + HeaderSize, framed.Length);
    }

    [Fact]
    public void FramedPayload_StartsWithTheMagicMarkerVersionAndAlgorithm()
    {
        var framed = Brotli().Compress(Payload());

        Assert.Equal((byte)'S', framed[0]);
        Assert.Equal((byte)'K', framed[1]);
        Assert.Equal((byte)'C', framed[2]);
        Assert.Equal(1, framed[3]);
        Assert.Equal((byte)CompressionAlgorithm.Brotli, framed[4]);
        Assert.Equal(Payload().Length, BinaryPrimitives.ReadInt64LittleEndian(framed.AsSpan(5)));
    }

    [Fact]
    public void AnUnknownFrameVersion_ReportsMalformed()
    {
        // Forward compatibility: a payload written by a future version fails loudly rather than
        // being misread under today's rules.
        var framed = Brotli().Compress(Payload());
        framed[3] = 99;

        var result = Brotli().Decompress(framed);

        Assert.True(result.IsFailure);
        Assert.Equal(CompressionErrorCodes.MalformedPayload, result.Error.Code);
    }

    [Fact]
    public void AnUnknownAlgorithmInTheFrame_ReportsAlgorithmMismatch()
    {
        var framed = Brotli().Compress(Payload());
        framed[4] = 77;

        var result = Brotli().Decompress(framed);

        Assert.True(result.IsFailure);
        Assert.Equal(CompressionErrorCodes.AlgorithmMismatch, result.Error.Code);
    }

    [Fact]
    public void ANegativeDeclaredLength_ReportsMalformed()
    {
        var framed = Brotli().Compress(Payload());
        BinaryPrimitives.WriteInt64LittleEndian(framed.AsSpan(5), -42);

        var result = Brotli().Decompress(framed);

        Assert.True(result.IsFailure);
        Assert.Equal(CompressionErrorCodes.MalformedPayload, result.Error.Code);
    }

    [Fact]
    public void SpanAndByteArrayOverloads_AgreeOnEveryPath()
    {
        var compressor = Brotli();
        var payload = Payload();

        Assert.Equal(compressor.Compress(payload), compressor.Compress(payload.AsSpan()));

        var compressed = compressor.Compress(payload);
        var fromArray = compressor.Decompress(compressed);
        var fromSpan = compressor.Decompress(compressed.AsSpan());

        Assert.True(fromArray.IsSuccess);
        Assert.True(fromSpan.IsSuccess);
        Assert.Equal(fromArray.Value, fromSpan.Value);
    }

    [Fact]
    public void BufferWriterOverloads_RoundTrip()
    {
        var compressor = Brotli();
        var payload = Payload();

        var compressed = new ArrayBufferWriter<byte>();
        compressor.Compress(payload, compressed);

        var decompressed = new ArrayBufferWriter<byte>();
        var result = compressor.Decompress(compressed.WrittenSpan, decompressed);

        Assert.True(result.IsSuccess);
        Assert.Equal(payload, decompressed.WrittenSpan.ToArray());
    }

    [Fact]
    public void BufferWriterOverload_ProducesTheSameBytesAsTheByteArrayOverload()
    {
        var compressor = Brotli();
        var payload = Payload();

        var written = new ArrayBufferWriter<byte>();
        compressor.Compress(payload, written);

        Assert.Equal(compressor.Compress(payload), written.WrittenSpan.ToArray());
    }

    [Fact]
    public void BufferWriterOverloads_RejectANullWriter()
    {
        var compressor = Brotli();

        Assert.Throws<ArgumentNullException>(() => compressor.Compress(Payload(), null!));
        Assert.Throws<ArgumentNullException>(() => compressor.Decompress(Payload(), null!));
    }

    [Fact]
    public void BufferWriterDecompress_TruncatedPayload_Fails()
    {
        var compressor = Brotli();
        var compressed = compressor.Compress(Payload());
        var output = new ArrayBufferWriter<byte>();

        var result = compressor.Decompress(compressed.AsSpan(0, compressed.Length / 2), output);

        Assert.True(result.IsFailure);
        Assert.Equal(CompressionErrorCodes.TruncatedPayload, result.Error.Code);
    }

    [Fact]
    public void SpanOverload_TreatsANullArrayAsEmpty_RatherThanThrowing()
    {
        // Documented asymmetry: a null array widens to an empty span, so the span overload cannot
        // reject it the way the byte[] overload does.
        var compressor = Brotli();

        var compressed = compressor.Compress(((byte[])null!).AsSpan());
        var result = compressor.Decompress(compressed);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
    }
}

using System.Text;
using Microsoft.Extensions.Options;
using SharedKernel.Compression.Options;
using Xunit;

namespace SharedKernel.Compression.Tests;

/// <summary>
/// Where the frame's recorded length comes from on the stream path, which decides whether reading the
/// payload back can detect truncation.
/// </summary>
public sealed class StreamFramingTests
{
    private static byte[] Payload() => Encoding.UTF8.GetBytes(string.Join(",", Enumerable.Range(0, 20000)));

    private static IPayloadCompressor Create(CompressionFraming framing = CompressionFraming.Framed) =>
        new BrotliPayloadCompressor(Microsoft.Extensions.Options.Options.Create(new CompressionOptions()), framing);

    [Fact]
    public void SeekableInput_RecordsTheLength_SoTruncationIsDetected()
    {
        var compressor = Create();
        using var compressed = new MemoryStream();
        using (var input = new MemoryStream(Payload()))
        {
            compressor.Compress(input, compressed);
        }

        var result = compressor.Decompress(compressed.ToArray()[..(int)(compressed.Length / 2)]);

        Assert.True(result.IsFailure);
        Assert.Equal(CompressionErrorCodes.TruncatedPayload, result.Error.Code);
    }

    [Fact]
    public void NonSeekableInput_ButSeekableOutput_StillRecordsTheLength()
    {
        // The length is patched back into the header after compressing, so truncation stays detectable.
        var compressor = Create();
        var payload = Payload();

        using var compressed = new MemoryStream();
        using (var input = new ReadOnlyNonSeekableStream(payload))
        {
            compressor.Compress(input, compressed);
        }

        var bytes = compressed.ToArray();
        Assert.Equal(
            payload.Length,
            System.Buffers.Binary.BinaryPrimitives.ReadInt64LittleEndian(bytes.AsSpan(5)));

        var truncated = compressor.Decompress(bytes[..(bytes.Length / 2)]);
        Assert.True(truncated.IsFailure);
        Assert.Equal(CompressionErrorCodes.TruncatedPayload, truncated.Error.Code);

        var whole = compressor.Decompress(bytes);
        Assert.True(whole.IsSuccess);
        Assert.Equal(payload, whole.Value);
    }

    [Fact]
    public async Task NonSeekableInput_ButSeekableOutput_StillRecordsTheLength_Async()
    {
        var compressor = Create();
        var payload = Payload();

        using var compressed = new MemoryStream();
        using (var input = new ReadOnlyNonSeekableStream(payload))
        {
            await compressor.CompressAsync(input, compressed);
        }

        var result = compressor.Decompress(compressed.ToArray());

        Assert.True(result.IsSuccess);
        Assert.Equal(payload, result.Value);
        Assert.Equal(
            payload.Length,
            System.Buffers.Binary.BinaryPrimitives.ReadInt64LittleEndian(compressed.ToArray().AsSpan(5)));
    }

    [Fact]
    public void NeitherStreamSeekable_RoundTripsButCannotDetectTruncation()
    {
        // Pins the one documented hole: with no way to learn the length before or after writing, the
        // frame records "unknown" and the length check has nothing to compare against.
        var compressor = Create();
        var payload = Payload();

        var sink = new MemoryStream();
        using (var input = new ReadOnlyNonSeekableStream(payload))
        using (var output = new WriteOnlyNonSeekableStream(sink))
        {
            compressor.Compress(input, output);
        }

        var bytes = sink.ToArray();
        Assert.Equal(-1, System.Buffers.Binary.BinaryPrimitives.ReadInt64LittleEndian(bytes.AsSpan(5)));

        var whole = compressor.Decompress(bytes);
        Assert.True(whole.IsSuccess);
        Assert.Equal(payload, whole.Value);

        var truncated = compressor.Decompress(bytes[..(bytes.Length / 2)]);
        Assert.True(truncated.IsSuccess);
        Assert.True(truncated.Value.Length < payload.Length);
    }

    [Fact]
    public void Compress_ReadsFromTheStreamsCurrentPosition_NotItsStart()
    {
        var compressor = Create();
        var payload = Payload();
        const int skip = 100;

        using var compressed = new MemoryStream();
        using (var input = new MemoryStream(payload) { Position = skip })
        {
            compressor.Compress(input, compressed);
        }

        var result = compressor.Decompress(compressed.ToArray());

        Assert.True(result.IsSuccess);
        Assert.Equal(payload.Length - skip, result.Value.Length);
        Assert.Equal(payload.AsSpan(skip).ToArray(), result.Value);
    }

    [Fact]
    public void Compress_DoesNotDisposeTheCallerStreams()
    {
        var compressor = Create();
        using var input = new MemoryStream(Payload());
        using var output = new MemoryStream();

        compressor.Compress(input, output);

        // Both remain usable: a disposed MemoryStream throws on Position.
        Assert.True(output.Position > 0);
        Assert.Equal(input.Length, input.Position);
    }

    [Fact]
    public void Compress_AppendsToAnOutputStreamThatAlreadyHasContent()
    {
        // The header is written at the output's current position, and patched there — not at offset 0.
        var compressor = Create();
        var payload = Payload();
        var prefix = Encoding.UTF8.GetBytes("PREFIX");

        using var output = new MemoryStream();
        output.Write(prefix);
        using (var input = new ReadOnlyNonSeekableStream(payload))
        {
            compressor.Compress(input, output);
        }

        var bytes = output.ToArray();
        Assert.Equal(prefix, bytes[..prefix.Length]);

        var result = compressor.Decompress(bytes[prefix.Length..]);
        Assert.True(result.IsSuccess);
        Assert.Equal(payload, result.Value);
    }

    private sealed class ReadOnlyNonSeekableStream(byte[] data) : Stream
    {
        private readonly MemoryStream _inner = new(data);

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() { }

        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _inner.Dispose();

            base.Dispose(disposing);
        }
    }

    private sealed class WriteOnlyNonSeekableStream(Stream inner) : Stream
    {
        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => inner.Flush();

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);
    }
}

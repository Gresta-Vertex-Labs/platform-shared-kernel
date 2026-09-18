using System.Buffers;

namespace SharedKernel.Compression.Internal;

/// <summary>
/// A write-only <see cref="Stream"/> over an <see cref="IBufferWriter{T}"/>, so the compression
/// streams — which only speak <see cref="Stream"/> — can write straight into a caller-supplied
/// buffer writer instead of through an intermediate <see cref="MemoryStream"/> and a copy out of it.
/// </summary>
internal sealed class BufferWriterStream(IBufferWriter<byte> writer) : Stream
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

    public override void Write(ReadOnlySpan<byte> buffer) => writer.Write(buffer);

    public override void Write(byte[] buffer, int offset, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        writer.Write(buffer.AsSpan(offset, count));
    }

    public override void WriteByte(byte value)
    {
        writer.GetSpan(1)[0] = value;
        writer.Advance(1);
    }

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
            return ValueTask.FromCanceled(cancellationToken);

        writer.Write(buffer.Span);
        return ValueTask.CompletedTask;
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        return WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    }

    public override void Flush()
    {
        // Every write lands in the writer immediately; there is nothing buffered here to flush.
    }

    public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();
}

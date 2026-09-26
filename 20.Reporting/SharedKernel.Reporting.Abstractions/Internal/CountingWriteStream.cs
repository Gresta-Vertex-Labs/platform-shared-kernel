namespace SharedKernel.Reporting.Internal;

/// <summary>
/// A write-only pass-through that counts the bytes written. Never disposes the inner stream. Once
/// <paramref name="abort"/> is cancelled every write throws, so a writer that ignores its own token still stops when
/// the consumer (an upload) has gone away.
/// </summary>
internal sealed class CountingWriteStream(Stream inner, CancellationToken abort = default) : Stream
{
    private readonly Stream _inner = inner ?? throw new ArgumentNullException(nameof(inner));

    public long BytesWritten { get; private set; }

    public override bool CanRead => false;

    public override bool CanSeek => false;

    public override bool CanWrite => true;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => BytesWritten;
        set => throw new NotSupportedException();
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        abort.ThrowIfCancellationRequested();
        _inner.Write(buffer, offset, count);
        BytesWritten += count;
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        abort.ThrowIfCancellationRequested();
        _inner.Write(buffer);
        BytesWritten += buffer.Length;
    }

    public override void WriteByte(byte value)
    {
        abort.ThrowIfCancellationRequested();
        _inner.WriteByte(value);
        BytesWritten++;
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        abort.ThrowIfCancellationRequested();
        await _inner.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
        BytesWritten += buffer.Length;
    }

    public override void Flush() => _inner.Flush();

    public override Task FlushAsync(CancellationToken cancellationToken) => _inner.FlushAsync(cancellationToken);

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();
}

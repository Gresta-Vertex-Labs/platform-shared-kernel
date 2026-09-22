using System.Security.Cryptography;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Storage.S3.Tests.Infrastructure;

internal static class TestData
{
    public static string UniqueKey(string name = "file.bin") => $"{Guid.NewGuid():N}/{name}";

    public static byte[] Bytes(int length, int seed = 7)
    {
        var bytes = new byte[length];
        new Random(seed).NextBytes(bytes);
        return bytes;
    }

    public static string Sha256(byte[] bytes) => Convert.ToBase64String(SHA256.HashData(bytes));

    public static async Task<byte[]> ReadAllAsync(IFileStorage storage, string key, FileDownloadOptions? options = null)
    {
        Result<FileDownload> download = await storage.DownloadAsync(key, options);
        if (download.IsFailure)
        {
            throw new InvalidOperationException(download.Error.ToString());
        }

        await using FileDownload file = download.Value;
        using var buffer = new MemoryStream();
        await file.Content.CopyToAsync(buffer);
        return buffer.ToArray();
    }

    public static T Ok<T>(this Result<T> result) =>
        result.IsSuccess ? result.Value : throw new InvalidOperationException($"Expected success, got {result.Error}.");
}

/// <summary>A stream that can only be read forward, like a pipe or a network response.</summary>
internal sealed class ForwardOnlyStream(byte[] content) : Stream
{
    private readonly MemoryStream _inner = new(content);

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        _inner.ReadAsync(buffer, cancellationToken);

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

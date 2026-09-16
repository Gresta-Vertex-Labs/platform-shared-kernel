using System.Collections.Concurrent;
using System.Security.Cryptography;
using SharedKernel.Cryptography.Hashing;

namespace SharedKernel.Testing.Cryptography;

/// <summary>A test double for <see cref="IContentHasher"/> that computes real SHA-256 digests and records the content.</summary>
public sealed class FakeContentHasher : IContentHasher
{
    private readonly ConcurrentQueue<byte[]> _hashed = new();

    /// <summary>Every buffer hashed so far, streams included, in order.</summary>
    public IReadOnlyList<byte[]> HashedContent => [.. _hashed];

    /// <inheritdoc />
    public byte[] ComputeHash(ReadOnlySpan<byte> content)
    {
        _hashed.Enqueue(content.ToArray());
        return SHA256.HashData(content);
    }

    /// <inheritdoc />
    public byte[] ComputeHash(Stream content)
    {
        ArgumentNullException.ThrowIfNull(content);

        using var buffer = new MemoryStream();
        content.CopyTo(buffer);
        return ComputeHash(buffer.ToArray());
    }

    /// <inheritdoc />
    public async ValueTask<byte[]> ComputeHashAsync(Stream content, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        return ComputeHash(buffer.ToArray());
    }
}

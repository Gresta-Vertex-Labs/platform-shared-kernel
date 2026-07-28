using System.Collections.Concurrent;
using System.Security.Cryptography;
using SharedKernel.Cryptography.Hashing;

namespace SharedKernel.Testing.Cryptography;

/// <summary>
/// In-memory test double for <see cref="IContentHasher"/>.
/// </summary>
/// <remarks>
/// Mirrors <see cref="Sha256ContentHasher"/> exactly (real <see cref="SHA256.HashData(byte[])"/>)
/// since content hashing is already fast/deterministic and there is no reason to fake the
/// algorithm — the sole value-add is completeness and introspection via <see cref="HashedContent"/>.
/// The stream overloads fully buffer their input before hashing (so the payload can be recorded for
/// introspection), unlike production's genuinely constant-memory streaming — an accepted trade-off
/// for a test double, since test payloads are never blob-scale.
/// </remarks>
public sealed class FakeContentHasher : IContentHasher
{
    private readonly ConcurrentQueue<byte[]> _hashedContent = new();

    /// <summary>Every content payload ever hashed via <see cref="ComputeHash(byte[])"/>/<see cref="ComputeHash(Stream)"/>/<see cref="ComputeHashAsync"/>, append-only.</summary>
    public IReadOnlyList<byte[]> HashedContent => _hashedContent.ToArray();

    /// <inheritdoc />
    public byte[] ComputeHash(byte[] content)
    {
        ArgumentNullException.ThrowIfNull(content);

        _hashedContent.Enqueue(content);
        return SHA256.HashData(content);
    }

    /// <inheritdoc />
    public byte[] ComputeHash(Stream content)
    {
        ArgumentNullException.ThrowIfNull(content);

        byte[] buffer = ReadAllBytes(content);
        _hashedContent.Enqueue(buffer);
        return SHA256.HashData(buffer);
    }

    /// <inheritdoc />
    public async ValueTask<byte[]> ComputeHashAsync(Stream content, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        byte[] buffer = await ReadAllBytesAsync(content, cancellationToken).ConfigureAwait(false);
        _hashedContent.Enqueue(buffer);
        return SHA256.HashData(buffer);
    }

    private static byte[] ReadAllBytes(Stream content)
    {
        using var buffer = new MemoryStream();
        content.CopyTo(buffer);
        return buffer.ToArray();
    }

    private static async Task<byte[]> ReadAllBytesAsync(Stream content, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        return buffer.ToArray();
    }
}

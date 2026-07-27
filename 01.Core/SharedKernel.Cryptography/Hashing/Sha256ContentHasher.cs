using System.Security.Cryptography;

namespace SharedKernel.Cryptography.Hashing;

/// <summary>
/// Computes a fast, non-salted, non-iterated SHA-256 digest of arbitrary content, backed by the
/// BCL one-shot <see cref="SHA256.HashData(byte[])"/>/<see cref="SHA256.HashData(Stream)"/>/
/// <see cref="SHA256.HashDataAsync(Stream, CancellationToken)"/> static APIs.
/// </summary>
/// <remarks>
/// Stateless and thread-safe. Never use this type — or a raw <see cref="SHA256"/> call anywhere
/// else in the platform — for hashing a secret; see <see cref="IContentHasher"/>'s remarks and
/// use <see cref="IOneWayHasher"/> instead for that case. This implementation is
/// algorithm-swappable by construction: a future second digest could register a second
/// <see cref="IContentHasher"/> implementation without any change to the contract itself.
/// </remarks>
public sealed class Sha256ContentHasher : IContentHasher
{
    /// <inheritdoc />
    public byte[] ComputeHash(byte[] content)
    {
        ArgumentNullException.ThrowIfNull(content);

        return SHA256.HashData(content);
    }

    /// <inheritdoc />
    public byte[] ComputeHash(Stream content)
    {
        ArgumentNullException.ThrowIfNull(content);

        return SHA256.HashData(content);
    }

    /// <inheritdoc />
    public async ValueTask<byte[]> ComputeHashAsync(Stream content, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        return await SHA256.HashDataAsync(content, cancellationToken).ConfigureAwait(false);
    }
}

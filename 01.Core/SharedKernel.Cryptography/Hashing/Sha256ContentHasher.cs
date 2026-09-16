using System.Security.Cryptography;

namespace SharedKernel.Cryptography.Hashing;

/// <summary><see cref="IContentHasher"/> backed by <see cref="SHA256"/>.</summary>
/// <remarks>Stateless and thread-safe.</remarks>
public sealed class Sha256ContentHasher : IContentHasher
{
    /// <inheritdoc />
    public byte[] ComputeHash(ReadOnlySpan<byte> content) => SHA256.HashData(content);

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

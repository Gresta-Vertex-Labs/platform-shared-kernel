namespace SharedKernel.Cryptography.Hashing;

/// <summary>Encodes <see cref="IContentHasher"/> digests as text.</summary>
public static class ContentHasherExtensions
{
    /// <summary>Computes the digest of <paramref name="content"/> as lowercase hexadecimal.</summary>
    /// <param name="hasher">The content hasher.</param>
    /// <param name="content">The content to hash.</param>
    /// <returns>A 64-character lowercase hexadecimal string.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="hasher"/> is <see langword="null"/>.</exception>
    public static string ComputeHashHex(this IContentHasher hasher, ReadOnlySpan<byte> content)
    {
        ArgumentNullException.ThrowIfNull(hasher);
        return Convert.ToHexStringLower(hasher.ComputeHash(content));
    }

    /// <summary>Computes the digest of <paramref name="content"/> as standard padded Base64.</summary>
    /// <param name="hasher">The content hasher.</param>
    /// <param name="content">The content to hash.</param>
    /// <returns>A 44-character Base64 string.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="hasher"/> is <see langword="null"/>.</exception>
    public static string ComputeHashBase64(this IContentHasher hasher, ReadOnlySpan<byte> content)
    {
        ArgumentNullException.ThrowIfNull(hasher);
        return Convert.ToBase64String(hasher.ComputeHash(content));
    }
}

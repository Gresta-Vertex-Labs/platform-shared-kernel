namespace SharedKernel.Cryptography.Hashing;

/// <summary>
/// Convenience string-encoding extension methods for <see cref="IContentHasher"/>.
/// </summary>
/// <remarks>
/// Both methods are built on <see cref="IContentHasher.ComputeHash(byte[])"/> — they add encoding
/// only, never a second hashing strategy. As with <see cref="IContentHasher"/> itself, never use
/// these for secrets; use <see cref="IOneWayHasher"/> instead.
/// </remarks>
public static class ContentHasherExtensions
{
    /// <summary>
    /// Computes the digest of <paramref name="content"/> and encodes it as lowercase hexadecimal.
    /// </summary>
    /// <param name="hasher">The content hasher.</param>
    /// <param name="content">The content to hash. Never a secret.</param>
    /// <returns>The digest as a lowercase hex-encoded string.</returns>
    public static string ComputeHashHex(this IContentHasher hasher, byte[] content)
    {
        ArgumentNullException.ThrowIfNull(hasher);

        byte[] digest = hasher.ComputeHash(content);
        return Convert.ToHexStringLower(digest);
    }

    /// <summary>
    /// Computes the digest of <paramref name="content"/> and encodes it as Base64.
    /// </summary>
    /// <param name="hasher">The content hasher.</param>
    /// <param name="content">The content to hash. Never a secret.</param>
    /// <returns>The digest as a Base64-encoded string.</returns>
    public static string ComputeHashBase64(this IContentHasher hasher, byte[] content)
    {
        ArgumentNullException.ThrowIfNull(hasher);

        byte[] digest = hasher.ComputeHash(content);
        return Convert.ToBase64String(digest);
    }
}

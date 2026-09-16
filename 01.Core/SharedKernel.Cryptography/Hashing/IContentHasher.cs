namespace SharedKernel.Cryptography.Hashing;

/// <summary>Computes a fast SHA-256 digest of content that is not secret.</summary>
/// <remarks>
/// <para>
/// Use it for checksums, ETags, deduplication keys, cache keys derived from a body, and hash chains over audit
/// records — cases where speed matters and the input is not something an attacker would try to guess.
/// </para>
/// <para>
/// <b>Never use it for passwords, API keys, recovery codes or other secrets.</b> A fast unsalted digest lets an
/// attacker test billions of guesses per second. Use <see cref="IOneWayHasher"/>, which is salted and deliberately
/// slow.
/// </para>
/// </remarks>
public interface IContentHasher
{
    /// <summary>Computes the digest of <paramref name="content"/>.</summary>
    /// <param name="content">The content to hash.</param>
    /// <returns>The 32-byte digest.</returns>
    byte[] ComputeHash(ReadOnlySpan<byte> content);

    /// <summary>Computes the digest of a stream, reading it to the end without buffering it in memory.</summary>
    /// <param name="content">The stream to read.</param>
    /// <returns>The 32-byte digest.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="content"/> is <see langword="null"/>.</exception>
    byte[] ComputeHash(Stream content);

    /// <summary>Computes the digest of a stream asynchronously, reading it to the end without buffering it in memory.</summary>
    /// <param name="content">The stream to read.</param>
    /// <param name="cancellationToken">A token to cancel reading.</param>
    /// <returns>The 32-byte digest.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="content"/> is <see langword="null"/>.</exception>
    ValueTask<byte[]> ComputeHashAsync(Stream content, CancellationToken cancellationToken = default);
}

namespace SharedKernel.Cryptography.Hashing;

/// <summary>
/// Computes a fast, non-salted, non-iterated cryptographic digest of arbitrary content for
/// non-secret content-fingerprinting use cases.
/// </summary>
/// <remarks>
/// <para>
/// <b>This contract is the deliberate architectural opposite of <see cref="IOneWayHasher"/>.</b>
/// <see cref="IOneWayHasher"/> is intentionally slow (600,000 PBKDF2 iterations by default) to
/// resist brute-force attacks on secrets — exactly the wrong tool, both performance-wise and
/// semantically, for hashing a 50MB upload to compute its ETag. <see cref="IContentHasher"/>
/// exists for the opposite class of problem: object-storage ETags/checksums, content-addressable
/// deduplication keys, and cache-key derivation from a payload body, where the input is not a
/// secret and speed matters far more than resistance to offline brute-forcing.
/// </para>
/// <para>
/// <b>Never use <see cref="IContentHasher"/> for passwords, API keys, recovery codes, or any
/// other secret.</b> Use <see cref="IOneWayHasher"/> for those instead. The two contracts must
/// never be conflated or merged into one — they serve deliberately opposite
/// performance/security profiles (fast+single-pass vs. slow+salted+iterated).
/// </para>
/// </remarks>
public interface IContentHasher
{
    /// <summary>
    /// Computes the digest of <paramref name="content"/>.
    /// </summary>
    /// <param name="content">The content to hash. Never a secret — see remarks on <see cref="IContentHasher"/>.</param>
    /// <returns>The raw digest bytes.</returns>
    byte[] ComputeHash(byte[] content);

    /// <summary>
    /// Computes the digest of <paramref name="content"/>, reading it as a stream so the full
    /// content is never materialized in memory at once.
    /// </summary>
    /// <param name="content">The content stream to hash. Never a secret — see remarks on <see cref="IContentHasher"/>.</param>
    /// <returns>The raw digest bytes.</returns>
    byte[] ComputeHash(Stream content);

    /// <summary>
    /// Asynchronously computes the digest of <paramref name="content"/>, reading it as a stream
    /// so the full content is never materialized in memory at once. Intended for large blob
    /// uploads where synchronous, blocking stream reads would be undesirable.
    /// </summary>
    /// <param name="content">The content stream to hash. Never a secret — see remarks on <see cref="IContentHasher"/>.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The raw digest bytes.</returns>
    ValueTask<byte[]> ComputeHashAsync(Stream content, CancellationToken cancellationToken = default);
}

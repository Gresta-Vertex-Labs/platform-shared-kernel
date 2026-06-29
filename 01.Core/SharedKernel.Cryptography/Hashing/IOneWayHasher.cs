namespace SharedKernel.Cryptography.Hashing;

/// <summary>
/// Hashes and verifies one-way secrets using a self-describing, versioned encoding so that the
/// iteration count or algorithm can be strengthened in the future without invalidating
/// already-stored hashes.
/// </summary>
/// <remarks>
/// This is a secret-agnostic contract: a password is one example consumer, not the sole purpose.
/// It is equally suited to API keys, recovery codes, security-question answers, or any other
/// one-way, slow, salted-hash-then-verify secret. Implementations must never use raw
/// <c>SHA256</c>/<c>SHA512</c>/<c>MD5</c> for one-way secret hashing — only a deliberately slow,
/// salted key-derivation function.
/// </remarks>
public interface IOneWayHasher
{
    /// <summary>
    /// Hashes <paramref name="secret"/>, producing a self-describing encoded string that
    /// embeds the algorithm identity, iteration count, and a freshly generated salt.
    /// </summary>
    /// <param name="secret">The plaintext secret to hash (e.g., a password or API key).</param>
    /// <returns>A Base64-encoded, self-describing hash string safe to persist verbatim.</returns>
    string Hash(string secret);

    /// <summary>
    /// Verifies <paramref name="secret"/> against a previously produced <paramref name="hash"/>.
    /// </summary>
    /// <param name="hash">The stored hash string previously produced by <see cref="Hash(string)"/>.</param>
    /// <param name="secret">The plaintext secret to verify.</param>
    /// <returns>
    /// <see cref="HashVerificationResult.Success"/> if the secret matches and the hash is
    /// current; <see cref="HashVerificationResult.SuccessRehashNeeded"/> if it matches but
    /// was produced with an older configuration; <see cref="HashVerificationResult.Failed"/>
    /// otherwise.
    /// </returns>
    HashVerificationResult Verify(string hash, string secret);
}

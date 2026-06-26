namespace SharedKernel.Cryptography.Hashing;

/// <summary>
/// Hashes and verifies passwords using a self-describing, versioned encoding so that the
/// iteration count or algorithm can be strengthened in the future without invalidating
/// already-stored hashes.
/// </summary>
/// <remarks>
/// Implementations must never use raw <c>SHA256</c>/<c>SHA512</c>/<c>MD5</c> for password
/// hashing — only a deliberately slow, salted key-derivation function.
/// </remarks>
public interface IPasswordHasher
{
    /// <summary>
    /// Hashes <paramref name="password"/>, producing a self-describing encoded string that
    /// embeds the algorithm identity, iteration count, and a freshly generated salt.
    /// </summary>
    /// <param name="password">The plaintext password to hash.</param>
    /// <returns>A Base64-encoded, self-describing hash string safe to persist verbatim.</returns>
    string Hash(string password);

    /// <summary>
    /// Verifies <paramref name="password"/> against a previously produced <paramref name="hash"/>.
    /// </summary>
    /// <param name="hash">The stored hash string previously produced by <see cref="Hash(string)"/>.</param>
    /// <param name="password">The plaintext password to verify.</param>
    /// <returns>
    /// <see cref="PasswordVerificationResult.Success"/> if the password matches and the hash is
    /// current; <see cref="PasswordVerificationResult.SuccessRehashNeeded"/> if it matches but
    /// was produced with an older configuration; <see cref="PasswordVerificationResult.Failed"/>
    /// otherwise.
    /// </returns>
    PasswordVerificationResult Verify(string hash, string password);
}

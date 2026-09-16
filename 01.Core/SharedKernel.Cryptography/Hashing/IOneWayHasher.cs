namespace SharedKernel.Cryptography.Hashing;

/// <summary>Hashes and verifies secrets that must never be recoverable: passwords, API keys, recovery codes.</summary>
/// <remarks>
/// <para>
/// Hashes are PHC strings that name their algorithm, cost and pepper, so the configuration can be strengthened at
/// any time. <see cref="Verify"/> keeps accepting hashes produced under the old configuration and returns
/// <see cref="HashVerificationResult.SuccessRehashNeeded"/>; hash the secret again at that point and store the result.
/// </para>
/// <para>
/// Secrets are normalized to Unicode NFKC before hashing, so the same password typed on different keyboards or
/// operating systems verifies. Never hash secrets with <see cref="IContentHasher"/>, SHA-256 or MD5.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// string stored = hasher.Hash(password);
///
/// switch (hasher.Verify(stored, attempt))
/// {
///     case HashVerificationResult.SuccessRehashNeeded:
///         await users.UpdatePasswordHashAsync(userId, hasher.Hash(attempt));
///         goto case HashVerificationResult.Success;
///     case HashVerificationResult.Success:
///         return SignIn(userId);
///     default:
///         return InvalidCredentials();
/// }
/// </code>
/// </example>
public interface IOneWayHasher
{
    /// <summary>Hashes <paramref name="secret"/> with the configured algorithm, cost and pepper.</summary>
    /// <param name="secret">The secret. Must not be empty.</param>
    /// <returns>A PHC string, safe to store.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="secret"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="secret"/> is empty.</exception>
    string Hash(string secret);

    /// <summary>Verifies <paramref name="secret"/> against a stored hash.</summary>
    /// <param name="hash">The stored hash.</param>
    /// <param name="secret">The secret to check.</param>
    /// <returns>The outcome. Malformed input returns <see cref="HashVerificationResult.Failed"/> and never throws.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="hash"/> or <paramref name="secret"/> is <see langword="null"/>.</exception>
    HashVerificationResult Verify(string hash, string secret);
}
